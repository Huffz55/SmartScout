using HtmlAgilityPack;
using System.Collections.Generic;
using System.Globalization;

namespace SmartScout.Infrastructure.Services.Scraping;

public record PerGameMetrics(
    int GamesPlayed, 
    decimal? Minutes, 
    decimal? Points, 
    decimal? Assists, 
    decimal? Rebounds, 
    decimal? Steals, 
    decimal? Blocks, 
    decimal? FgPct, 
    decimal? Fg3Pct, 
    decimal? FtPct, 
    decimal? Turnovers);

public record AdvancedMetrics(
    decimal? TrueShootingPct, 
    decimal? PlayerEfficiencyRating, 
    decimal? UsagePct, 
    decimal? TotalReboundPct, 
    decimal? AssistPct, 
    decimal? WinShares, 
    decimal? BoxPlusMinus);

public class BasketballReferenceDomParser
{
    public Dictionary<int, PerGameMetrics> ParsePerGameTable(HtmlDocument doc)
    {
        var seasonData = new Dictionary<int, PerGameMetrics>();

        var table = doc.DocumentNode.SelectSingleNode("//table[@id='per_game']") 
                    ?? doc.DocumentNode.SelectSingleNode("//table[contains(@id, 'per_game')]");

        if (table == null) return seasonData;

        var rows = table.SelectNodes(".//tbody/tr");
        if (rows == null) return seasonData;

        foreach (var row in rows)
        {
            var seasonNode = row.SelectSingleNode(".//th[@data-stat='year_id']") 
                          ?? row.SelectSingleNode(".//th[@data-stat='season']") 
                          ?? row.SelectSingleNode(".//td[@data-stat='year_id']") 
                          ?? row.SelectSingleNode(".//td[@data-stat='season']");

            if (seasonNode == null) continue;

            string seasonText = seasonNode.InnerText;
            var parts = seasonText.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int startYear))
            {
                int parsedSeason = startYear + 1;

                // TOT combined row is rendered first on trade seasons; deduplicate subsequent stint rows
                if (!seasonData.ContainsKey(parsedSeason))
                {
                    int gp = ParseInt(row.SelectSingleNode(".//td[@data-stat='g']")?.InnerText);
                    decimal? minutes = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='mp_per_g']")?.InnerText);
                    decimal? pts = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='pts_per_g']")?.InnerText);
                    decimal? ast = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='ast_per_g']")?.InnerText);
                    decimal? reb = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='trb_per_g']")?.InnerText);
                    decimal? stl = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='stl_per_g']")?.InnerText);
                    decimal? blk = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='blk_per_g']")?.InnerText);
                    decimal? fgPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='fg_pct']")?.InnerText);
                    decimal? fg3Pct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='fg3_pct']")?.InnerText);
                    decimal? ftPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='ft_pct']")?.InnerText);
                    decimal? tov = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='tov_per_g']")?.InnerText);

                    seasonData[parsedSeason] = new PerGameMetrics(gp, minutes, pts, ast, reb, stl, blk, fgPct, fg3Pct, ftPct, tov);
                }
            }
        }

        return seasonData;
    }

    public Dictionary<int, AdvancedMetrics> ParseAdvancedTable(HtmlDocument doc)
    {
        var advSeasonData = new Dictionary<int, AdvancedMetrics>();

        var advTable = doc.DocumentNode.SelectSingleNode("//table[@id='advanced']") 
                    ?? doc.DocumentNode.SelectSingleNode("//table[contains(@id, 'advanced')]");

        // Basketball-Reference defers secondary table rendering by wrapping them in HTML comments
        if (advTable == null)
        {
            var comments = doc.DocumentNode.SelectNodes("//comment()");
            if (comments != null)
            {
                foreach (var comment in comments)
                {
                    var commentContent = (comment is HtmlCommentNode cNode ? cNode.Comment : comment.InnerHtml)?.Trim() ?? string.Empty;
                    if (commentContent.Contains("id=\"advanced\""))
                    {
                        if (commentContent.StartsWith("<!--"))
                            commentContent = commentContent.Substring(4);
                        if (commentContent.EndsWith("-->"))
                            commentContent = commentContent.Substring(0, commentContent.Length - 3);

                        var tempDoc = new HtmlDocument();
                        tempDoc.LoadHtml(commentContent);
                        advTable = tempDoc.DocumentNode.SelectSingleNode("//table[@id='advanced']");
                        if (advTable != null) break;
                    }
                }
            }
        }

        if (advTable == null) return advSeasonData;

        var advRows = advTable.SelectNodes(".//tbody/tr");
        if (advRows == null) return advSeasonData;

        foreach (var row in advRows)
        {
            var seasonNode = row.SelectSingleNode(".//th[@data-stat='year_id']") 
                          ?? row.SelectSingleNode(".//th[@data-stat='season']") 
                          ?? row.SelectSingleNode(".//td[@data-stat='year_id']") 
                          ?? row.SelectSingleNode(".//td[@data-stat='season']");

            if (seasonNode == null) continue;

            string seasonText = seasonNode.InnerText;
            var parts = seasonText.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int startYear))
            {
                int parsedSeason = startYear + 1;

                if (!advSeasonData.ContainsKey(parsedSeason))
                {
                    decimal? tsPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='ts_pct']")?.InnerText);
                    decimal? per = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='per']")?.InnerText);
                    decimal? usgPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='usg_pct']")?.InnerText);
                    decimal? trbPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='trb_pct']")?.InnerText);
                    decimal? astPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='ast_pct']")?.InnerText);
                    decimal? ws = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='ws']")?.InnerText);
                    decimal? bpm = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='bpm']")?.InnerText);

                    advSeasonData[parsedSeason] = new AdvancedMetrics(tsPct, per, usgPct, trbPct, astPct, ws, bpm);
                }
            }
        }

        return advSeasonData;
    }

    private static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return decimal.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val) 
            ? val 
            : null;
    }

    private static int ParseInt(string? text, int defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return defaultValue;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) 
            ? val 
            : defaultValue;
    }
}
