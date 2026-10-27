using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using SmartScout.Domain.Entities;

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
    decimal? Turnovers,
    string? TeamName = null,
    int? GamesStarted = null,
    decimal? Fgm = null,
    decimal? Fga = null,
    decimal? Fg3m = null,
    decimal? Fg3a = null,
    decimal? Fg2m = null,
    decimal? Fg2a = null,
    decimal? Fg2Pct = null,
    decimal? EffectiveFgPct = null,
    decimal? Ftm = null,
    decimal? Fta = null,
    decimal? Orb = null,
    decimal? Drb = null,
    decimal? PersonalFouls = null,
    string? League = null,
    int? Age = null,
    string? Position = null,
    string? Nationality = null);

public record AdvancedMetrics(
    decimal? TrueShootingPct, 
    decimal? PlayerEfficiencyRating, 
    decimal? UsagePct, 
    decimal? TotalReboundPct, 
    decimal? AssistPct, 
    decimal? WinShares, 
    decimal? BoxPlusMinus);

public record LeagueSeasonPlayerRow(
    string FirstName,
    string LastName,
    string? ExternalSlug,
    string? Position,
    string TeamName,
    PerGameMetrics Metrics,
    int? Age = null,
    string? Nationality = null);

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
                    seasonData[parsedSeason] = ExtractRowPerGameMetrics(row);
                }
            }
        }

        return seasonData;
    }

    public List<(int Season, PerGameMetrics Metrics)> ParseStintPerGameRows(HtmlDocument doc)
    {
        var stints = new List<(int Season, PerGameMetrics Metrics)>();

        var table = doc.DocumentNode.SelectSingleNode("//table[@id='per_game']") 
                    ?? doc.DocumentNode.SelectSingleNode("//table[contains(@id, 'per_game')]");

        if (table == null) return stints;

        var rows = table.SelectNodes(".//tbody/tr");
        if (rows == null) return stints;

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
                var metrics = ExtractRowPerGameMetrics(row);
                if (metrics.GamesPlayed > 0)
                {
                    stints.Add((parsedSeason, metrics));
                }
            }
        }

        return stints;
    }

    /// <summary>
    /// Parses a league-wide season stats page from either Basketball-Reference (/international/, /gleague/, /leagues/)
    /// or RealGM (/international/league/, /ncaa/, /dleague/).
    /// </summary>
    public List<LeagueSeasonPlayerRow> ParseLeagueSeasonTable(HtmlDocument doc, string leagueCode)
    {
        var results = new List<LeagueSeasonPlayerRow>();

        // 1. Check Basketball-Reference data-stat tables first (including HTML comment-wrapped tables)
        var bRefTable = FindTableInDomOrComments(doc, "//table[contains(@id, 'per_game') or contains(@id, 'totals') or contains(@class, 'stats_table')]");
        if (bRefTable != null)
        {
            var rows = bRefTable.SelectNodes(".//tbody/tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    if (row.GetAttributeValue("class", "").Contains("thead"))
                        continue;

                    var playerNode = row.SelectSingleNode(".//*[@data-stat='player']")
                                  ?? row.SelectSingleNode(".//*[@data-stat='name_display']");
                    if (playerNode == null) continue;

                    string rawName = WebUtility.HtmlDecode(playerNode.InnerText).Replace("*", "").Trim();
                    if (string.IsNullOrWhiteSpace(rawName) || rawName.Equals("Player", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var (firstName, lastName) = SplitFullName(rawName);
                    string? slug = playerNode.GetAttributeValue("data-append-csv", null)
                                ?? playerNode.SelectSingleNode(".//a")?.GetAttributeValue("href", null);

                    var metrics = ExtractRowPerGameMetrics(row, leagueCode);
                    if (metrics.GamesPlayed <= 0) continue;

                    string? teamName = metrics.TeamName;
                    if (string.IsNullOrWhiteSpace(teamName) || teamName.EndsWith("Team", StringComparison.OrdinalIgnoreCase))
                    {
                        var teamLink = row.SelectSingleNode(".//a[contains(@href, '/teams/')]")
                                    ?? row.SelectSingleNode(".//*[@data-stat='team_name']//a")
                                    ?? row.SelectSingleNode(".//*[@data-stat='team_name']");
                        if (teamLink != null && !string.IsNullOrWhiteSpace(teamLink.InnerText))
                        {
                            teamName = WebUtility.HtmlDecode(teamLink.InnerText).Trim();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(teamName))
                    {
                        teamName = $"{leagueCode} Team";
                    }

                    string resolvedPos = !string.IsNullOrWhiteSpace(metrics.Position)
                                      ? metrics.Position 
                                      : (!string.IsNullOrWhiteSpace(row.SelectSingleNode(".//*[@data-stat='pos']")?.InnerText)
                                          ? row.SelectSingleNode(".//*[@data-stat='pos']")!.InnerText.Trim()
                                          : InferPositionFromMetrics(metrics));

                    int? age = metrics.Age ?? ParseNullableInt(row.SelectSingleNode(".//*[@data-stat='age']")?.InnerText);

                    string? nat = !string.IsNullOrWhiteSpace(metrics.Nationality)
                                ? metrics.Nationality
                                : (!string.IsNullOrWhiteSpace(row.SelectSingleNode(".//*[@data-stat='nationality']")?.InnerText)
                                    ? row.SelectSingleNode(".//*[@data-stat='nationality']")!.InnerText.Trim()
                                    : (!string.IsNullOrWhiteSpace(row.SelectSingleNode(".//*[@data-stat='birth_country']")?.InnerText)
                                        ? row.SelectSingleNode(".//*[@data-stat='birth_country']")!.InnerText.Trim()
                                        : Player.DetectNationality(firstName, lastName, teamName)));

                    results.Add(new LeagueSeasonPlayerRow(firstName, lastName, slug, resolvedPos, teamName, metrics, age, nat));
                }

                if (results.Count > 0)
                    return results;
            }
        }

        // 2. Check RealGM tables (table-striped, table-bordered, tablesaw, or table with Player/Team/GP/MPG/PPG headers)
        var realGmTable = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'table-striped') or contains(@class, 'table-bordered') or contains(@class, 'tablesaw') or .//th[contains(., 'MPG') or contains(., 'PPG')]]");
        if (realGmTable != null)
        {
            var headerNodes = realGmTable.SelectNodes(".//thead/tr/th") ?? realGmTable.SelectNodes(".//tr/th");
            var bodyRows = realGmTable.SelectNodes(".//tbody/tr") ?? realGmTable.SelectNodes(".//tr[td]");
            if (headerNodes != null && bodyRows != null)
            {
                var headers = headerNodes
                    .Select((th, idx) => (Name: WebUtility.HtmlDecode(th.InnerText).Replace("#", "").Trim().ToUpperInvariant(), Index: idx))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .GroupBy(x => x.Name)
                    .ToDictionary(g => g.Key, g => g.First().Index);

                foreach (var row in bodyRows)
                {
                    var cells = row.SelectNodes("./td");
                    if (cells == null || cells.Count < 6) continue;

                    string? GetCell(string colName)
                    {
                        if (headers.TryGetValue(colName, out int idx) && idx < cells.Count)
                            return WebUtility.HtmlDecode(cells[idx].InnerText).Trim();
                        return null;
                    }

                    string? rawName = GetCell("PLAYER");
                    if (string.IsNullOrWhiteSpace(rawName)) continue;

                    var (firstName, lastName) = SplitFullName(rawName);
                    string? playerSlug = row.SelectSingleNode(".//a[contains(@href, '/player/')]")?.GetAttributeValue("href", null);
                    string teamName = GetCell("TEAM") 
                                   ?? row.SelectSingleNode(".//a[contains(@href, '/teams/')]")?.InnerText?.Trim()
                                   ?? $"{leagueCode} Team";
                    teamName = WebUtility.HtmlDecode(teamName).Trim();

                    int gp = ParseInt(GetCell("GP"));
                    if (gp <= 0) continue;

                    decimal? mpg = ParseDecimal(GetCell("MPG") ?? GetCell("MIN"));
                    decimal? ppg = ParseDecimal(GetCell("PPG") ?? GetCell("PTS"));
                    decimal? fgm = ParseDecimal(GetCell("FGM"));
                    decimal? fga = ParseDecimal(GetCell("FGA"));
                    decimal? fgPct = NormalizePct(ParseDecimal(GetCell("FG%")));
                    decimal? fg3m = ParseDecimal(GetCell("3PM"));
                    decimal? fg3a = ParseDecimal(GetCell("3PA"));
                    decimal? fg3Pct = NormalizePct(ParseDecimal(GetCell("3P%")));
                    decimal? ftm = ParseDecimal(GetCell("FTM"));
                    decimal? fta = ParseDecimal(GetCell("FTA"));
                    decimal? ftPct = NormalizePct(ParseDecimal(GetCell("FT%")));
                    decimal? orb = ParseDecimal(GetCell("ORB") ?? GetCell("OFF"));
                    decimal? drb = ParseDecimal(GetCell("DRB") ?? GetCell("DEF"));
                    decimal? rpg = ParseDecimal(GetCell("RPG") ?? GetCell("REB"));
                    decimal? apg = ParseDecimal(GetCell("APG") ?? GetCell("AST"));
                    decimal? spg = ParseDecimal(GetCell("SPG") ?? GetCell("STL"));
                    decimal? bpg = ParseDecimal(GetCell("BPG") ?? GetCell("BLK"));
                    decimal? tov = ParseDecimal(GetCell("TOV") ?? GetCell("TO"));
                    decimal? pf = ParseDecimal(GetCell("PF"));

                    int? age = ParseNullableInt(GetCell("AGE"));
                    string? pos = GetCell("POS") ?? GetCell("POSITION");
                    string? nat = GetCell("NAT") ?? GetCell("COUNTRY") ?? Player.DetectNationality(firstName, lastName, teamName);

                    decimal? fg2m = (fgm.HasValue && fg3m.HasValue) ? Math.Max(0m, fgm.Value - fg3m.Value) : null;
                    decimal? fg2a = (fga.HasValue && fg3a.HasValue) ? Math.Max(0m, fga.Value - fg3a.Value) : null;
                    decimal? fg2Pct = (fg2m.HasValue && fg2a.HasValue && fg2a.Value > 0) ? Math.Round(fg2m.Value / fg2a.Value, 3) : null;

                    var metrics = new PerGameMetrics(
                        GamesPlayed: gp,
                        Minutes: mpg,
                        Points: ppg,
                        Assists: apg,
                        Rebounds: rpg,
                        Steals: spg,
                        Blocks: bpg,
                        FgPct: fgPct,
                        Fg3Pct: fg3Pct,
                        FtPct: ftPct,
                        Turnovers: tov,
                        TeamName: teamName,
                        GamesStarted: null,
                        Fgm: fgm,
                        Fga: fga,
                        Fg3m: fg3m,
                        Fg3a: fg3a,
                        Fg2m: fg2m,
                        Fg2a: fg2a,
                        Fg2Pct: fg2Pct,
                        EffectiveFgPct: null,
                        Ftm: ftm,
                        Fta: fta,
                        Orb: orb,
                        Drb: drb,
                        PersonalFouls: pf,
                        League: leagueCode,
                        Age: age,
                        Position: pos,
                        Nationality: nat);

                    string resolvedPos = pos ?? InferPositionFromMetrics(metrics);
                    results.Add(new LeagueSeasonPlayerRow(firstName, lastName, playerSlug, resolvedPos, teamName, metrics, age, nat));
                }
            }
        }

        return results;
    }

    public static string InferPositionFromMetrics(PerGameMetrics pg)
    {
        if (pg.Assists >= 3.8m || pg.Fg3a >= 3.5m) return "G";
        if (pg.Rebounds >= 7.5m && (pg.Fg3a ?? 0m) <= 1.2m) return "C";
        if (pg.Rebounds >= 5.0m) return "F";
        if (pg.Points >= 11.0m) return "G-F";
        return "F";
    }

    private static HtmlNode? FindTableInDomOrComments(HtmlDocument doc, string xpath)
    {
        var table = doc.DocumentNode.SelectSingleNode(xpath);
        if (table != null) return table;

        var comments = doc.DocumentNode.SelectNodes("//comment()");
        if (comments != null)
        {
            foreach (var comment in comments)
            {
                var content = (comment is HtmlCommentNode cNode ? cNode.Comment : comment.InnerHtml)?.Trim() ?? string.Empty;
                if (content.Contains("<table"))
                {
                    if (content.StartsWith("<!--")) content = content.Substring(4);
                    if (content.EndsWith("-->")) content = content.Substring(0, content.Length - 3);

                    var tempDoc = new HtmlDocument();
                    tempDoc.LoadHtml(content);
                    table = tempDoc.DocumentNode.SelectSingleNode(xpath);
                    if (table != null) return table;
                }
            }
        }

        return null;
    }

    private static (string FirstName, string LastName) SplitFullName(string rawName)
    {
        var parts = rawName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return ("Unknown", "Player");
        if (parts.Length == 1) return (parts[0], parts[0]);
        return (parts[0], string.Join(" ", parts.Skip(1)));
    }

    private static decimal? NormalizePct(decimal? pct)
    {
        if (!pct.HasValue) return null;
        if (pct.Value > 1.0m) return Math.Round(pct.Value / 100.0m, 3);
        return Math.Round(pct.Value, 3);
    }

    private static PerGameMetrics ExtractRowPerGameMetrics(HtmlNode row, string? defaultLeague = null)
    {
        int gp = ParseInt(
            (row.SelectSingleNode(".//td[@data-stat='games']")
          ?? row.SelectSingleNode(".//td[@data-stat='g']")
          ?? row.SelectSingleNode(".//td[@data-stat='games_played']"))?.InnerText);
        int? gs = ParseNullableInt(
            (row.SelectSingleNode(".//td[@data-stat='games_started']")
          ?? row.SelectSingleNode(".//td[@data-stat='gs']"))?.InnerText);
        decimal? minutes = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='mp_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='mp']"))?.InnerText);
        decimal? pts = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='pts_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='pts']"))?.InnerText);
        decimal? ast = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='ast_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='ast']"))?.InnerText);
        decimal? reb = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='trb_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='trb']"))?.InnerText);
        decimal? stl = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='stl_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='stl']"))?.InnerText);
        decimal? blk = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='blk_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='blk']"))?.InnerText);
        decimal? fgm = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='fg_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='fg']"))?.InnerText);
        decimal? fga = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='fga_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='fga']"))?.InnerText);
        decimal? fgPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='fg_pct']")?.InnerText);
        decimal? fg3m = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='fg3_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='fg3']"))?.InnerText);
        decimal? fg3a = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='fg3a_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='fg3a']"))?.InnerText);
        decimal? fg3Pct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='fg3_pct']")?.InnerText);
        decimal? fg2m = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='fg2_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='fg2']"))?.InnerText);
        decimal? fg2a = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='fg2a_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='fg2a']"))?.InnerText);
        decimal? fg2Pct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='fg2_pct']")?.InnerText);
        decimal? eFgPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='efg_pct']")?.InnerText);
        decimal? ftm = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='ft_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='ft']"))?.InnerText);
        decimal? fta = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='fta_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='fta']"))?.InnerText);
        decimal? ftPct = ParseDecimal(row.SelectSingleNode(".//td[@data-stat='ft_pct']")?.InnerText);
        decimal? orb = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='orb_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='orb']"))?.InnerText);
        decimal? drb = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='drb_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='drb']"))?.InnerText);
        decimal? tov = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='tov_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='tov']"))?.InnerText);
        decimal? pf = ParseDecimal(
            (row.SelectSingleNode(".//td[@data-stat='pf_per_g']") ?? row.SelectSingleNode(".//td[@data-stat='pf']"))?.InnerText);

        string? team = (row.SelectSingleNode(".//*[@data-stat='team_name']")
                     ?? row.SelectSingleNode(".//*[@data-stat='team_name_abbr']")
                     ?? row.SelectSingleNode(".//*[@data-stat='team_id']")
                     ?? row.SelectSingleNode(".//*[@data-stat='team']")
                     ?? row.SelectSingleNode(".//a[contains(@href, '/teams/')]"))?.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(team))
        {
            team = WebUtility.HtmlDecode(team).Trim();
        }

        int? age = ParseNullableInt(row.SelectSingleNode(".//*[@data-stat='age']")?.InnerText);
        string? pos = (row.SelectSingleNode(".//*[@data-stat='pos']")
                    ?? row.SelectSingleNode(".//*[@data-stat='position']"))?.InnerText?.Trim();
        string? nat = (row.SelectSingleNode(".//*[@data-stat='nationality']")
                    ?? row.SelectSingleNode(".//*[@data-stat='birth_country']")
                    ?? row.SelectSingleNode(".//*[@data-stat='country']"))?.InnerText?.Trim();

        string? league = (row.SelectSingleNode(".//td[@data-stat='comp_name_abbr']") 
                       ?? row.SelectSingleNode(".//td[@data-stat='lg_id']"))?.InnerText?.Trim() ?? defaultLeague;

        return new PerGameMetrics(
            gp, minutes, pts, ast, reb, stl, blk, fgPct, fg3Pct, ftPct, tov, team,
            gs, fgm, fga, fg3m, fg3a, fg2m, fg2a, fg2Pct, eFgPct, ftm, fta, orb, drb, pf, league, age, pos, nat);
    }

    public Dictionary<int, AdvancedMetrics> ParseAdvancedTable(HtmlDocument doc)
    {
        var advSeasonData = new Dictionary<int, AdvancedMetrics>();

        var advTable = FindTableInDomOrComments(doc, "//table[@id='advanced' or contains(@id, 'advanced')]");
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

    private static int? ParseNullableInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) 
            ? val 
            : null;
    }
}
