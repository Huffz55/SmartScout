using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Infrastructure.Services;

public class BasketballReferenceScrapingStrategy : ISeasonStatSyncStrategy
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BasketballReferenceScrapingStrategy> _logger;

    public BasketballReferenceScrapingStrategy(HttpClient httpClient, ILogger<BasketballReferenceScrapingStrategy> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<IEnumerable<PlayerSeasonStat>> FetchSeasonStatsAsync(List<Player> players, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var chunk in players.Chunk(30))
        {
            if (cancellationToken.IsCancellationRequested) break;

            var batchStats = new List<PlayerSeasonStat>();

            foreach (var player in chunk)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (string.IsNullOrWhiteSpace(player.LastName) || string.IsNullOrWhiteSpace(player.FirstName))
                    continue;

                string cleanLastName = new string(player.LastName.Where(char.IsLetter).ToArray()).ToLowerInvariant();
                string cleanFirstName = new string(player.FirstName.Where(char.IsLetter).ToArray()).ToLowerInvariant();

                if (cleanLastName.Length == 0 || cleanFirstName.Length == 0)
                    continue;

                string slugLastName = cleanLastName.Length > 5 ? cleanLastName.Substring(0, 5) : cleanLastName;
                string slugFirstName = cleanFirstName.Length > 2 ? cleanFirstName.Substring(0, 2) : cleanFirstName;
                string initial = slugLastName.Substring(0, 1);

                string slug = $"{initial}/{slugLastName}{slugFirstName}01.html";
                string url = $"https://www.basketball-reference.com/players/{slug}";

                try
                {
                    var response = await _httpClient.GetAsync(url, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var html = await response.Content.ReadAsStringAsync(cancellationToken);
                        var doc = new HtmlDocument();
                        doc.LoadHtml(html);

                        var seasonData = new Dictionary<int, (int gp, string min, decimal? pts, decimal? ast, decimal? reb, decimal? stl, decimal? blk, decimal? fg, decimal? fg3, decimal? ft, decimal? tov)>();
                        
                        var table = doc.DocumentNode.SelectSingleNode("//table[@id='per_game']") 
                                    ?? doc.DocumentNode.SelectSingleNode("//table[contains(@id, 'per_game')]");

                        if (table == null)
                        {
                            var comments = doc.DocumentNode.SelectNodes("//comment()");
                            if (comments != null)
                            {
                                foreach (var comment in comments)
                                {
                                    if (comment.InnerText.Contains("id=\"per_game\""))
                                    {
                                        var tempDoc = new HtmlDocument();
                                        tempDoc.LoadHtml(comment.InnerText);
                                        table = tempDoc.DocumentNode.SelectSingleNode("//table[@id='per_game']");
                                        if (table != null) break;
                                    }
                                }
                            }
                        }

                        if (table != null)
                        {
                            var rows = table.SelectNodes(".//tbody/tr");
                            if (rows != null)
                            {
                                foreach (var row in rows)
                                {
                                    var seasonNode = row.SelectSingleNode(".//th[@data-stat='season']") ?? row.SelectSingleNode(".//td[@data-stat='season']");
                                    if (seasonNode != null)
                                    {
                                        string seasonText = seasonNode.InnerText;
                                        var parts = seasonText.Split('-');
                                        if (parts.Length == 2 && int.TryParse(parts[0], out int startYear))
                                        {
                                            int parsedSeason = startYear + 1;

                                            if (!seasonData.ContainsKey(parsedSeason))
                                            {
                                                int.TryParse(row.SelectSingleNode(".//td[@data-stat='g']")?.InnerText, out int gp);
                                                string minutes = row.SelectSingleNode(".//td[@data-stat='mp_per_g']")?.InnerText ?? "0";
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='pts_per_g']")?.InnerText, out decimal pts);
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='ast_per_g']")?.InnerText, out decimal ast);
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='trb_per_g']")?.InnerText, out decimal reb);
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='stl_per_g']")?.InnerText, out decimal stl);
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='blk_per_g']")?.InnerText, out decimal blk);
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='fg_pct']")?.InnerText, out decimal fgPct);
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='fg3_pct']")?.InnerText, out decimal fg3Pct);
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='ft_pct']")?.InnerText, out decimal ftPct);
                                                decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='tov_per_g']")?.InnerText, out decimal tov);
                                                
                                                seasonData[parsedSeason] = (gp, minutes, pts, ast, reb, stl, blk, fgPct, fg3Pct, ftPct, tov);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        var advSeasonData = new Dictionary<int, (decimal? ts, decimal? per, decimal? usg, decimal? trb, decimal? ast, decimal? ws, decimal? bpm)>();

                        var advTable = doc.DocumentNode.SelectSingleNode("//table[@id='advanced']") 
                                    ?? doc.DocumentNode.SelectSingleNode("//table[contains(@id, 'advanced')]");

                        if (advTable == null)
                        {
                            var comments = doc.DocumentNode.SelectNodes("//comment()");
                            if (comments != null)
                            {
                                foreach (var comment in comments)
                                {
                                    if (comment.InnerText.Contains("id=\"advanced\""))
                                    {
                                        var tempDoc = new HtmlDocument();
                                        tempDoc.LoadHtml(comment.InnerText);
                                        advTable = tempDoc.DocumentNode.SelectSingleNode("//table[@id='advanced']");
                                        if (advTable != null) break;
                                    }
                                }
                            }
                        }

                        if (advTable != null)
                        {
                            var advRows = advTable.SelectNodes(".//tbody/tr");
                            if (advRows != null)
                            {
                                foreach (var row in advRows)
                                {
                                    var seasonNode = row.SelectSingleNode(".//th[@data-stat='season']") ?? row.SelectSingleNode(".//td[@data-stat='season']");
                                    if (seasonNode != null)
                                    {
                                        string seasonText = seasonNode.InnerText;
                                        var parts = seasonText.Split('-');
                                        if (parts.Length == 2 && int.TryParse(parts[0], out int startYear))
                                        {
                                            int parsedSeason = startYear + 1;

                                            if (!advSeasonData.ContainsKey(parsedSeason))
                                            {
                                                decimal? tsPct = null, per = null, usgPct = null, trbPct = null, astPct = null, ws = null, bpm = null;
                                                if (decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='ts_pct']")?.InnerText, out decimal t)) tsPct = t;
                                                if (decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='per']")?.InnerText, out decimal p)) per = p;
                                                if (decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='usg_pct']")?.InnerText, out decimal u)) usgPct = u;
                                                if (decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='trb_pct']")?.InnerText, out decimal trb)) trbPct = trb;
                                                if (decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='ast_pct']")?.InnerText, out decimal a)) astPct = a;
                                                if (decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='ws']")?.InnerText, out decimal w)) ws = w;
                                                if (decimal.TryParse(row.SelectSingleNode(".//td[@data-stat='bpm']")?.InnerText, out decimal b)) bpm = b;
                                                
                                                advSeasonData[parsedSeason] = (tsPct, per, usgPct, trbPct, astPct, ws, bpm);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        foreach (var kvp in seasonData)
                        {
                            int s = kvp.Key;
                            var pg = kvp.Value;
                            advSeasonData.TryGetValue(s, out var adv);

                            var stat = new PlayerSeasonStat(
                                player.Id,
                                s,
                                pg.gp,
                                pg.min,
                                pg.pts,
                                pg.ast,
                                pg.reb,
                                pg.stl,
                                pg.blk,
                                pg.fg,
                                pg.fg3,
                                pg.ft,
                                pg.tov,
                                adv.ts,
                                adv.per,
                                adv.usg,
                                adv.trb,
                                adv.ast,
                                adv.ws,
                                adv.bpm
                            );
                            batchStats.Add(stat);
                        }

                        if (seasonData.Any())
                        {
                            _logger.LogInformation("Successfully parsed {Count} seasons for {First} {Last}", seasonData.Count, player.FirstName, player.LastName);
                        }
                        else
                        {
                            _logger.LogWarning("DOM Parse Failed: No seasons found for {First} {Last} at {Url}", player.FirstName, player.LastName, url);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("HTTP {StatusCode} for {First} {Last} at {Url}", response.StatusCode, player.FirstName, player.LastName, url);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while scraping stats for {First} {Last} at {Url}", player.FirstName, player.LastName, url);
                }

                await Task.Delay(2000, cancellationToken);
            }

            if (batchStats.Any())
            {
                yield return batchStats;
            }
        }
    }
}
