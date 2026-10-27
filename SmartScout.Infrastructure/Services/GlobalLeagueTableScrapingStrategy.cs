using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Options;
using SmartScout.Infrastructure.Services.EuroLeague;
using SmartScout.Infrastructure.Services.Scraping;

namespace SmartScout.Infrastructure.Services;

public class GlobalLeagueTableScrapingStrategy : IPlayerSyncStrategy, ISeasonStatSyncStrategy
{
    private readonly ILogger<GlobalLeagueTableScrapingStrategy> _logger;
    private readonly ScraperOptions _options;
    private readonly BasketballReferenceDomParser _domParser;

    public GlobalLeagueTableScrapingStrategy(
        ILogger<GlobalLeagueTableScrapingStrategy> logger,
        IOptions<ScraperOptions> options,
        BasketballReferenceDomParser domParser)
    {
        _logger = logger;
        _options = options.Value;
        _domParser = domParser;
    }

    public async IAsyncEnumerable<IEnumerable<Player>> FetchPlayersAsync(
        int cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        SyncFilterContext? filter = null)
    {
        await foreach (var batch in FetchSeasonStatsInternalAsync(new List<Player>(), cancellationToken, filter, playersOnly: true))
        {
            if (batch.DiscoveredPlayers.Any())
            {
                yield return batch.DiscoveredPlayers;
            }
        }
    }

    public async IAsyncEnumerable<SeasonStatSyncBatchResult> FetchSeasonStatsAsync(
        List<Player> players,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        SyncFilterContext? filter = null)
    {
        await foreach (var batch in FetchSeasonStatsInternalAsync(players, cancellationToken, filter, playersOnly: false))
        {
            yield return batch;
        }
    }

    private async IAsyncEnumerable<SeasonStatSyncBatchResult> FetchSeasonStatsInternalAsync(
        List<Player> existingPlayers,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        SyncFilterContext? filter,
        bool playersOnly)
    {
        var candidateLeagues = GlobalLeagueEndpointCatalog.GetTableScrapedLeagues()
            .Where(l => filter == null || filter.ShouldProcessLeague(l.LeagueCode))
            .ToList();

        if (!candidateLeagues.Any())
        {
            yield break;
        }

        var seasons = (_options.EuroSeasons?.Any() == true ? _options.EuroSeasons : new[] { 2019, 2020, 2021, 2022, 2023, 2024, 2025, 2026 })
            .Where(y => filter == null || filter.ShouldProcessSeason(y))
            .OrderBy(y => y)
            .ToArray();

        if (!seasons.Any())
        {
            yield break;
        }

        var playerByNormalizedName = existingPlayers
            .GroupBy(p => !string.IsNullOrWhiteSpace(p.NormalizedName) ? p.NormalizedName : Player.NormalizeName(p.FirstName, p.LastName), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var playerByExternalId = existingPlayers
            .GroupBy(p => p.ExternalApiId)
            .ToDictionary(g => g.Key, g => g.First());

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await PlaywrightBrowserHelper.LaunchBrowserWithFallbackAsync(playwright, _options.Headless, _logger);

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = _options.UserAgent
        });

        var page = await context.NewPageAsync();
        int safeDelayMs = Math.Max(2500, _options.DelayBetweenRequestsMs);

        foreach (var leagueDef in candidateLeagues)
        {
            if (cancellationToken.IsCancellationRequested) break;

            foreach (int year in seasons)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (filter != null && filter.IsAlreadyCompleted(leagueDef.LeagueCode, year))
                {
                    _logger.LogInformation("Skipping {League} season {Year} — already marked Completed in IngestionJobLogs.", leagueDef.LeagueCode, year);
                    continue;
                }

                string primaryUrl = leagueDef.BuildPrimaryUrl(year);
                string? fallbackUrl = leagueDef.BuildFallbackUrl(year);

                List<LeagueSeasonPlayerRow> rows = new();
                string? scrapeError = null;
                try
                {
                    rows = await ScrapeLeagueTableWithFallbackAsync(page, leagueDef.LeagueCode, year, primaryUrl, fallbackUrl, safeDelayMs, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to scrape {League} season {Year}", leagueDef.LeagueCode, year);
                    scrapeError = ex.Message;
                }

                if (scrapeError != null)
                {
                    yield return new SeasonStatSyncBatchResult(
                        stats: Array.Empty<PlayerSeasonStat>(),
                        phantomPlayerIdsToDelete: Array.Empty<Guid>(),
                        leagueCode: leagueDef.LeagueCode,
                        season: year,
                        discoveredPlayers: Array.Empty<Player>(),
                        jobStatus: "Failed",
                        errorMessage: scrapeError);
                    continue;
                }

                if (rows.Count == 0)
                {
                    _logger.LogWarning("No player rows found for {League} season {Year} at {Url}.", leagueDef.LeagueCode, year, primaryUrl);
                    continue;
                }

                var discoveredPlayers = new List<Player>();
                var statsBatch = new List<PlayerSeasonStat>();

                foreach (var row in rows)
                {
                    string normName = Player.NormalizeName(row.FirstName, row.LastName);
                    int deterministicId = EuroLeagueParserHelper.GenerateDeterministicId(
                        !string.IsNullOrWhiteSpace(row.ExternalSlug)
                            ? $"Intl_{row.ExternalSlug}"
                            : $"Intl_{leagueDef.LeagueCode}_{normName}");

                    Player? matchedPlayer = null;
                    if (!string.IsNullOrWhiteSpace(normName) && playerByNormalizedName.TryGetValue(normName, out var byName))
                    {
                        matchedPlayer = byName;
                    }
                    else if (playerByExternalId.TryGetValue(deterministicId, out var byId))
                    {
                        matchedPlayer = byId;
                    }

                    string posToUse = !string.IsNullOrWhiteSpace(row.Position)
                        ? row.Position
                        : (row.Metrics != null ? BasketballReferenceDomParser.InferPositionFromMetrics(row.Metrics) : "F");

                    string natToUse = (!string.IsNullOrWhiteSpace(row.Nationality)
                        ? row.Nationality
                        : EuroLeagueParserHelper.DetectNationality(row.FirstName, row.LastName, row.TeamName)) ?? "INTL";

                    int? ageToUse = row.Age ?? row.Metrics?.Age;
                    DateTime? birthDateToSet = null;
                    if (ageToUse.HasValue && ageToUse.Value > 0)
                    {
                        birthDateToSet = new DateTime(year - ageToUse.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    }
                    else if (matchedPlayer?.BirthDate != null)
                    {
                        birthDateToSet = matchedPlayer.BirthDate;
                    }

                    string teamToUse = (!string.IsNullOrWhiteSpace(row.TeamName) && !row.TeamName.EndsWith("Team", StringComparison.OrdinalIgnoreCase))
                        ? row.TeamName
                        : (!string.IsNullOrWhiteSpace(row.Metrics?.TeamName) && !row.Metrics.TeamName.EndsWith("Team", StringComparison.OrdinalIgnoreCase)
                            ? row.Metrics.TeamName
                            : $"{leagueDef.LeagueCode} Team");

                    if (matchedPlayer == null)
                    {
                        string extJson = JsonSerializer.Serialize(new
                        {
                            source = leagueDef.PrimaryProvider,
                            slug = row.ExternalSlug,
                            league = leagueDef.LeagueCode,
                            season = year
                        });

                        matchedPlayer = new Player(
                            externalApiId: deterministicId,
                            firstName: row.FirstName,
                            lastName: row.LastName,
                            position: posToUse,
                            height: null,
                            weightPounds: null,
                            teamName: teamToUse,
                            league: leagueDef.LeagueCode,
                            nationality: natToUse,
                            birthDate: birthDateToSet,
                            externalIdsJson: extJson);

                        playerByExternalId[deterministicId] = matchedPlayer;
                        if (!string.IsNullOrWhiteSpace(normName))
                        {
                            playerByNormalizedName[normName] = matchedPlayer;
                        }
                    }
                    else
                    {
                        matchedPlayer.UpdateDetails(
                            matchedPlayer.FirstName,
                            matchedPlayer.LastName,
                            posToUse,
                            matchedPlayer.Height,
                            matchedPlayer.WeightPounds,
                            teamToUse,
                            leagueDef.LeagueCode,
                            nationality: natToUse,
                            birthDate: birthDateToSet ?? matchedPlayer.BirthDate);
                    }

                    discoveredPlayers.Add(matchedPlayer);

                    if (!playersOnly && row.Metrics != null)
                    {
                        var pg = row.Metrics;
                        string rawJson = JsonSerializer.Serialize(new
                        {
                            source = leagueDef.PrimaryProvider,
                            url = primaryUrl,
                            season = year,
                            league = leagueDef.LeagueCode,
                            team = teamToUse,
                            metrics = pg
                        });

                        var stat = new PlayerSeasonStat(
                            playerId: matchedPlayer.Id,
                            season: year,
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
                            league: leagueDef.LeagueCode,
                            teamName: teamToUse,
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
                            rawScrapedPayload: rawJson);

                        statsBatch.Add(stat);
                    }
                }

                _logger.LogInformation("Successfully scraped {StatCount} player stints ({PlayerCount} players) for {League} season {Year}.",
                    statsBatch.Count, discoveredPlayers.Count, leagueDef.LeagueCode, year);

                yield return new SeasonStatSyncBatchResult(
                    stats: statsBatch,
                    phantomPlayerIdsToDelete: Array.Empty<Guid>(),
                    leagueCode: leagueDef.LeagueCode,
                    season: year,
                    discoveredPlayers: discoveredPlayers,
                    jobStatus: "Completed");
            }

            // Safe delay between leagues to strictly respect rate limits and ban protection
            await Task.Delay(2500, cancellationToken);
        }
    }

    private async Task<List<LeagueSeasonPlayerRow>> ScrapeLeagueTableWithFallbackAsync(
        IPage page,
        string leagueCode,
        int year,
        string primaryUrl,
        string? fallbackUrl,
        int safeDelayMs,
        CancellationToken cancellationToken)
    {
        var rows = await TryScrapeSingleUrlWithPollyRetryAsync(page, leagueCode, primaryUrl, safeDelayMs, cancellationToken);
        string effectiveUrl = primaryUrl;
        if (rows.Count == 0 && !string.IsNullOrWhiteSpace(fallbackUrl))
        {
            _logger.LogInformation("Primary URL {PrimaryUrl} returned 0 rows for {League} {Year}; attempting fallback URL {FallbackUrl}...",
                primaryUrl, leagueCode, year, fallbackUrl);

            rows = await TryScrapeSingleUrlWithPollyRetryAsync(page, leagueCode, fallbackUrl, safeDelayMs, cancellationToken);
            effectiveUrl = fallbackUrl;
        }

        // If RealGM table returned 100 players, fetch page 2 to capture full roster depth
        if (rows.Count >= 100 && effectiveUrl.Contains("realgm.com", StringComparison.OrdinalIgnoreCase) && effectiveUrl.Contains("/desc/1/"))
        {
            string page2Url = effectiveUrl.Replace("/desc/1/", "/desc/2/");
            try
            {
                var page2Rows = await TryScrapeSingleUrlWithPollyRetryAsync(page, leagueCode, page2Url, safeDelayMs, cancellationToken);
                if (page2Rows.Count > 0)
                {
                    rows.AddRange(page2Rows);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch RealGM page 2 for {Url}", page2Url);
            }
        }

        return rows;
    }

    private async Task<List<LeagueSeasonPlayerRow>> TryScrapeSingleUrlWithPollyRetryAsync(
        IPage page,
        string leagueCode,
        string url,
        int safeDelayMs,
        CancellationToken cancellationToken)
    {
        int[] retryBackoffSeconds = { 10, 25, 60 };

        for (int attempt = 0; attempt <= retryBackoffSeconds.Length; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await Task.Delay(safeDelayMs, cancellationToken);

                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });

                if (response == null)
                    return new List<LeagueSeasonPlayerRow>();

                if (response.Status == 429 || response.Status == 503)
                {
                    if (attempt < retryBackoffSeconds.Length)
                    {
                        int waitSeconds = retryBackoffSeconds[attempt];
                        _logger.LogWarning("HTTP {Status} rate-limit encountered at {Url}. Backing off for {WaitSec}s (Attempt {Attempt}/{Max})...",
                            response.Status, url, waitSeconds, attempt + 1, retryBackoffSeconds.Length);
                        await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
                        continue;
                    }
                    return new List<LeagueSeasonPlayerRow>();
                }

                if (response.Status == 404)
                {
                    return new List<LeagueSeasonPlayerRow>();
                }

                if (response.Ok)
                {
                    string html = await page.ContentAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    return _domParser.ParseLeagueSeasonTable(doc, leagueCode);
                }
            }
            catch (TimeoutException)
            {
                if (attempt < retryBackoffSeconds.Length)
                {
                    int waitSeconds = retryBackoffSeconds[attempt];
                    _logger.LogWarning("Timeout loading {Url}. Retrying after {WaitSec}s (Attempt {Attempt}/{Max})...",
                        url, waitSeconds, attempt + 1, retryBackoffSeconds.Length);
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
                    continue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error scraping league table at {Url}", url);
                break;
            }
        }

        return new List<LeagueSeasonPlayerRow>();
    }
}
