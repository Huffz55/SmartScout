using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Options;
using SmartScout.Infrastructure.Services.Scraping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Infrastructure.Services;

public class BasketballReferenceScrapingStrategy : ISeasonStatSyncStrategy
{
    private readonly ILogger<BasketballReferenceScrapingStrategy> _logger;
    private readonly ScraperOptions _options;
    private readonly BasketballReferenceUrlBuilder _urlBuilder;
    private readonly BasketballReferenceDomParser _domParser;

    public BasketballReferenceScrapingStrategy(
        ILogger<BasketballReferenceScrapingStrategy> logger,
        IOptions<ScraperOptions> options,
        BasketballReferenceUrlBuilder urlBuilder,
        BasketballReferenceDomParser domParser)
    {
        _logger = logger;
        _options = options.Value;
        _urlBuilder = urlBuilder;
        _domParser = domParser;
    }

    public async IAsyncEnumerable<SeasonStatSyncBatchResult> FetchSeasonStatsAsync(
        List<Player> players, 
        [EnumeratorCancellation] CancellationToken cancellationToken,
        SyncFilterContext? filter = null)
    {
        // NBA season stats are fast-synced via GlobalLeagueTableScrapingStrategy (3s vs 30min) unless player bio crawl is explicitly enabled
        if (!_options.EnableNbaPlayerBioScraping)
        {
            _logger.LogInformation("Skipping BasketballReferenceScrapingStrategy (individual player bio crawl). NBA stats are fast-synced via GlobalLeagueTableScrapingStrategy.");
            yield break;
        }

        // NBA player bio scraping runs only when explicitly targeted (e.g. ?league=NBA), not during general all-league sweeps
        if (filter != null && (!filter.ShouldProcessLeague("NBA") || (filter.IsAllLeagues && !filter.HasExplicitLeague("NBA"))))
        {
            yield break;
        }

        if (filter != null && filter.Season.HasValue && filter.IsAlreadyCompleted("NBA", filter.Season.Value))
        {
            _logger.LogInformation("Skipping NBA season {Season} — already marked Completed in IngestionJobLogs.", filter.Season.Value);
            yield break;
        }

        var nbaPlayers = players
            .Where(p => string.Equals(p.League, "NBA", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!nbaPlayers.Any())
        {
            yield break;
        }

        // Basketball-Reference enforces max 20 requests/minute => minimum 3500ms between requests (~17 req/min)
        int safeDelayMs = Math.Max(3500, _options.DelayBetweenRequestsMs);
        int[] retryBackoffSeconds = { 10, 25, 60 };

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await PlaywrightBrowserHelper.LaunchBrowserWithFallbackAsync(playwright, _options.Headless, _logger);
        
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions 
        {
            UserAgent = _options.UserAgent
        });
        
        var page = await context.NewPageAsync();

        foreach (var chunk in nbaPlayers.Chunk(30))
        {
            if (cancellationToken.IsCancellationRequested) break;

            var batchStats = new List<PlayerSeasonStat>();
            var phantomPlayerIds = new List<Guid>();

            foreach (var player in chunk)
            {
                if (cancellationToken.IsCancellationRequested) break;

                for (int suffixIndex = 1; suffixIndex <= _options.MaxSuffixAttempts; suffixIndex++)
                {
                    var urlInfo = _urlBuilder.GeneratePlayerUrl(player.FirstName, player.LastName, suffixIndex);
                    if (urlInfo == null) break;

                    string url = urlInfo.Value.url;

                    try
                    {
                        IResponse? response = null;
                        for (int attempt = 0; attempt <= retryBackoffSeconds.Length; attempt++)
                        {
                            response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                            if (response != null && (response.Status == 429 || response.Status == 503) && attempt < retryBackoffSeconds.Length)
                            {
                                int waitSec = retryBackoffSeconds[attempt];
                                _logger.LogWarning("HTTP {Status} rate limit at {Url}. Backing off for {WaitSec}s (Attempt {Attempt}/{Max})...",
                                    response.Status, url, waitSec, attempt + 1, retryBackoffSeconds.Length);
                                await Task.Delay(TimeSpan.FromSeconds(waitSec), cancellationToken);
                                continue;
                            }
                            break;
                        }
                        
                        if (response != null && response.Ok)
                        {
                            try 
                            {
                                await page.WaitForSelectorAsync("table[id*='per_game']", new PageWaitForSelectorOptions 
                                { 
                                    State = WaitForSelectorState.Attached, 
                                    Timeout = _options.SelectorTimeoutMs 
                                });
                                
                                var html = await page.ContentAsync();
                                var doc = new HtmlDocument();
                                doc.LoadHtml(html);

                                var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
                                string pagePlayerName = h1Node?.InnerText?.Trim() ?? string.Empty;

                                if (!_urlBuilder.IsPlayerMatch(pagePlayerName, player.FirstName, player.LastName))
                                {
                                    _logger.LogInformation("Slug collision detected at {Url} (Page is '{PageName}', expected '{First} {Last}'). Trying next index...", 
                                        url, pagePlayerName, player.FirstName, player.LastName);
                                    continue;
                                }

                                var perGameData = _domParser.ParsePerGameTable(doc);
                                var advancedData = _domParser.ParseAdvancedTable(doc);

                                foreach (var kvp in perGameData)
                                {
                                    int season = kvp.Key;
                                    if (filter != null && !filter.ShouldProcessSeason(season))
                                        continue;

                                    var pg = kvp.Value;
                                    advancedData.TryGetValue(season, out var adv);

                                    string rawJson = JsonSerializer.Serialize(new
                                    {
                                        source = "Basketball-Reference",
                                        url,
                                        season,
                                        perGame = pg,
                                        advanced = adv
                                    });

                                    var stat = new PlayerSeasonStat(
                                        playerId: player.Id,
                                        season: season,
                                        gamesPlayed: pg.GamesPlayed,
                                        minutes: pg.Minutes,
                                        points: pg.Points,
                                        assists: pg.Assists,
                                        rebounds: pg.Rebounds,
                                        steals: pg.Steals,
                                        blocks: pg.Blocks,
                                        fgPct: pg.FgPct,
                                        fg3Pct: pg.Fg3Pct,
                                        ftPct: pg.FtPct,
                                        turnovers: pg.Turnovers,
                                        trueShootingPct: adv?.TrueShootingPct,
                                        playerEfficiencyRating: adv?.PlayerEfficiencyRating,
                                        usagePct: adv?.UsagePct,
                                        totalReboundPct: adv?.TotalReboundPct,
                                        assistPct: adv?.AssistPct,
                                        winShares: adv?.WinShares,
                                        boxPlusMinus: adv?.BoxPlusMinus,
                                        league: string.IsNullOrWhiteSpace(pg.League) ? "NBA" : pg.League,
                                        teamName: pg.TeamName ?? player.TeamName ?? "TOT",
                                        competitionPhase: "Regular Season",
                                        gamesStarted: pg.GamesStarted,
                                        fgm: pg.Fgm,
                                        fga: pg.Fga,
                                        fg3m: pg.Fg3m,
                                        fg3a: pg.Fg3a,
                                        fg2m: pg.Fg2m,
                                        fg2a: pg.Fg2a,
                                        fg2Pct: pg.Fg2Pct,
                                        ftm: pg.Ftm,
                                        fta: pg.Fta,
                                        orb: pg.Orb,
                                        drb: pg.Drb,
                                        pfCommitted: pg.PersonalFouls,
                                        effectiveFgPct: pg.EffectiveFgPct,
                                        rawScrapedPayload: rawJson
                                    );
                                    batchStats.Add(stat);
                                }

                                if (perGameData.Any())
                                {
                                    _logger.LogInformation("Successfully parsed {Count} seasons for {First} {Last} at {Url}", perGameData.Count, player.FirstName, player.LastName, url);
                                }
                                else
                                {
                                    _logger.LogWarning("DOM Parse Failed: No seasons found for {First} {Last} at {Url}", player.FirstName, player.LastName, url);
                                }

                                break; // Suffix loop break on success
                            }
                            catch (TimeoutException)
                            {
                                _logger.LogWarning("Timeout waiting for table for {First} {Last}. Marked as phantom player for deletion.", player.FirstName, player.LastName);
                                phantomPlayerIds.Add(player.Id);
                                break;
                            }
                        }
                        else if (response != null && response.Status == 404)
                        {
                            _logger.LogWarning("404 Not Found for {First} {Last} at {Url}. Skipping.", player.FirstName, player.LastName, url);
                            break;
                        }
                        else
                        {
                            _logger.LogWarning("HTTP {StatusCode} for {First} {Last} at {Url}", response?.Status, player.FirstName, player.LastName, url);
                            break;
                        }
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("Timeout loading page for {First} {Last}. Skipping.", player.FirstName, player.LastName);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception while scraping stats for {First} {Last} at {Url}", player.FirstName, player.LastName, url);
                        break;
                    }
                }

                await Task.Delay(safeDelayMs, cancellationToken);
            }

            if (batchStats.Any() || phantomPlayerIds.Any())
            {
                yield return new SeasonStatSyncBatchResult(
                    stats: batchStats,
                    phantomPlayerIdsToDelete: phantomPlayerIds,
                    leagueCode: "NBA",
                    season: filter?.Season ?? 2026,
                    jobStatus: "Completed");
            }
        }
    }
}
