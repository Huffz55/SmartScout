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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions 
        { 
            Headless = _options.Headless 
        });
        
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions 
        {
            UserAgent = _options.UserAgent
        });
        
        var page = await context.NewPageAsync();

        foreach (var chunk in players.Chunk(30))
        {
            if (cancellationToken.IsCancellationRequested) break;

            var batchStats = new List<PlayerSeasonStat>();
            var phantomPlayerIds = new List<Guid>();

            foreach (var player in chunk)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Basketball-Reference only archives NBA players; European players are handled by EuroSeasonStatSyncStrategy
                if (!string.Equals(player.League, "NBA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (int suffixIndex = 1; suffixIndex <= _options.MaxSuffixAttempts; suffixIndex++)
                {
                    var urlInfo = _urlBuilder.GeneratePlayerUrl(player.FirstName, player.LastName, suffixIndex);
                    if (urlInfo == null) break;

                    string url = urlInfo.Value.url;

                    try
                    {
                        var response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                        
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
                                    var pg = kvp.Value;
                                    advancedData.TryGetValue(season, out var adv);

                                    var stat = new PlayerSeasonStat(
                                        player.Id,
                                        season,
                                        pg.GamesPlayed,
                                        pg.Minutes,
                                        pg.Points,
                                        pg.Assists,
                                        pg.Rebounds,
                                        pg.Steals,
                                        pg.Blocks,
                                        pg.FgPct,
                                        pg.Fg3Pct,
                                        pg.FtPct,
                                        pg.Turnovers,
                                        adv?.TrueShootingPct,
                                        adv?.PlayerEfficiencyRating,
                                        adv?.UsagePct,
                                        adv?.TotalReboundPct,
                                        adv?.AssistPct,
                                        adv?.WinShares,
                                        adv?.BoxPlusMinus
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

                await Task.Delay(_options.DelayBetweenRequestsMs, cancellationToken);
            }

            if (batchStats.Any() || phantomPlayerIds.Any())
            {
                yield return new SeasonStatSyncBatchResult(batchStats, phantomPlayerIds);
            }
        }
    }
}
