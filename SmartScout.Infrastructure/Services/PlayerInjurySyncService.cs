using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Infrastructure.Services;

/// <summary>
/// Synchronizes live and recent player injuries from open verified sports feeds
/// (ESPN NBA, Basketball-Reference NBA, Rotowire EuroLeague, Rotowire CBB/NBA, CBS Sports NBA).
/// </summary>
public partial class PlayerInjurySyncService : IPlayerInjurySyncService
{
    private const int CurrentActiveSeason = 2027;
    private const string EspnNbaInjuriesUrl = "https://site.api.espn.com/apis/site/v2/sports/basketball/nba/injuries";
    private const string BRefNbaInjuriesUrl = "https://www.basketball-reference.com/friv/injuries.fcgi";
    private const string RotowireEuroInjuriesUrl = "https://www.rotowire.com/euro/tables/injury-report.php?team=ALL&pos=ALL";
    private const string RotowireNbaInjuriesUrl = "https://www.rotowire.com/basketball/tables/injury-report.php?team=ALL&pos=ALL";
    private const string RotowireCbbInjuriesUrl = "https://www.rotowire.com/cbasketball/tables/injury-report.php?team=ALL&pos=ALL";
    private const string CbsNbaInjuriesUrl = "https://www.cbssports.com/nba/injuries/";
    private const string CoversNbaInjuriesUrl = "https://www.covers.com/sport/basketball/nba/injuries";
    private const string CoversNcaabInjuriesUrl = "https://www.covers.com/sport/basketball/ncaab/injuries";
    private const string PiratasAcbRssUrl = "https://piratasdelbasket.net/feed/";
    private const string EurohoopsEuroCupRssUrl = "https://www.eurohoops.net/en/eurocup/feed/";
    private const string EurohoopsBslRssUrl = "https://www.eurohoops.net/en/tbl/feed/";
    private const string EurohoopsAcbRssUrl = "https://www.eurohoops.net/en/acb/feed/";
    private const string EurohoopsLegaARssUrl = "https://www.eurohoops.net/en/legabasket/feed/";
    private const string EurohoopsBblRssUrl = "https://www.eurohoops.net/en/bbl/feed/";
    private const string BasketballSphereWpJsonUrl = "https://basketballsphere.com/en/wp-json/wp/v2/posts?search=injury&per_page=15&_fields=date,title,excerpt";
    private const string BasketfaulBslWpJsonUrl = "https://basketfaul.com.tr/wp-json/wp/v2/posts?search=sakat&per_page=15&_fields=date,title,excerpt";

    private static readonly HashSet<string> EuropeanLeagues = new(StringComparer.OrdinalIgnoreCase)
    {
        "EuroLeague", "EuroCup", "BSL", "ACB", "LegaA", "BBL", "BCL", "ABA", "GBL", "LKL", "LNB", "VTB"
    };

    private readonly ISmartScoutDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PlayerInjurySyncService> _logger;
    private readonly IScoutingCacheState? _cacheState;

    internal record RawInjuryRecord(
        string PlayerName,
        string PreferredLeague,
        string? TeamName,
        string InjuryType,
        string Status,
        DateTime ReportedAt,
        string Source,
        string? Notes);

    internal record PlayerCandidate(
        Guid Id,
        string FullName,
        string NormalizedName,
        string CompactName,
        string PrimaryLeague,
        string? TeamName,
        HashSet<string> ActiveLeagues);

    public PlayerInjurySyncService(
        ISmartScoutDbContext dbContext,
        HttpClient httpClient,
        ILogger<PlayerInjurySyncService> logger,
        IScoutingCacheState? cacheState = null)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _logger = logger;
        _cacheState = cacheState;
    }

    public async Task<InjurySyncSummaryDto> SyncLiveInjuriesAsync(CancellationToken cancellationToken = default)
    {
        var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fetched = new List<RawInjuryRecord>();

        var players = await _dbContext.Players
            .AsNoTracking()
            .Select(p => new
            {
                p.Id,
                p.FullName,
                p.NormalizedName,
                p.League,
                p.TeamName,
                ActiveLeagues = p.SeasonStats
                    .Where(s => s.Season >= 2026)
                    .Select(s => s.League)
                    .Distinct()
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var allCandidates = new List<PlayerCandidate>(players.Count);
        var byNorm = new Dictionary<string, List<PlayerCandidate>>(StringComparer.OrdinalIgnoreCase);
        var byCompact = new Dictionary<string, List<PlayerCandidate>>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in players)
        {
            var norm = string.IsNullOrWhiteSpace(p.NormalizedName) ? Player.NormalizeName(p.FullName, string.Empty) : p.NormalizedName;
            var compact = StripSuffixAndPunct(norm);
            var candidate = new PlayerCandidate(
                p.Id,
                p.FullName,
                norm,
                compact,
                p.League,
                p.TeamName,
                new HashSet<string>(p.ActiveLeagues, StringComparer.OrdinalIgnoreCase));

            allCandidates.Add(candidate);

            if (!byNorm.TryGetValue(norm, out var list))
            {
                list = new List<PlayerCandidate>();
                byNorm[norm] = list;
            }
            list.Add(candidate);

            if (!byCompact.TryGetValue(compact, out var cList))
            {
                cList = new List<PlayerCandidate>();
                byCompact[compact] = cList;
            }
            cList.Add(candidate);
        }

        var acbCandidates = allCandidates
            .Where(c => string.Equals(c.PrimaryLeague, "ACB", StringComparison.OrdinalIgnoreCase) || c.ActiveLeagues.Contains("ACB"))
            .ToList();

        var euroCandidates = allCandidates
            .Where(c => EuropeanLeagues.Contains(c.PrimaryLeague) || c.ActiveLeagues.Any(l => EuropeanLeagues.Contains(l)))
            .ToList();

        await TryCollectAsync("ESPN NBA", () => FetchEspnNbaAsync(cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("BRef NBA", () => FetchBRefNbaAsync(cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Rotowire EuroLeague", () => FetchRotowireAsync(RotowireEuroInjuriesUrl, "EuroLeague", "Rotowire EuroLeague", cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("CBS NBA", () => FetchCbsNbaAsync(cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Rotowire NBA", () => FetchRotowireAsync(RotowireNbaInjuriesUrl, "NBA", "Rotowire NBA", cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Rotowire NCAA", () => FetchRotowireAsync(RotowireCbbInjuriesUrl, "NCAAD1", "Rotowire NCAA", cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Covers NBA", () => FetchCoversInjuriesAsync(CoversNbaInjuriesUrl, "NBA", "Covers NBA", cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Covers NCAAB", () => FetchCoversInjuriesAsync(CoversNcaabInjuriesUrl, "NCAAD1", "Covers NCAAB", cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Piratas ACB", () => FetchPiratasAcbParteMedicoAsync(acbCandidates, cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Eurohoops EuroCup", () => FetchEurohoopsRssAsync(EurohoopsEuroCupRssUrl, "EuroCup", "Eurohoops EuroCup", euroCandidates, cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Eurohoops BSL", () => FetchEurohoopsRssAsync(EurohoopsBslRssUrl, "BSL", "Eurohoops BSL", euroCandidates, cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Eurohoops ACB", () => FetchEurohoopsRssAsync(EurohoopsAcbRssUrl, "ACB", "Eurohoops ACB", acbCandidates, cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Eurohoops LegaA", () => FetchEurohoopsRssAsync(EurohoopsLegaARssUrl, "LegaA", "Eurohoops LegaA", euroCandidates, cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Eurohoops BBL", () => FetchEurohoopsRssAsync(EurohoopsBblRssUrl, "BBL", "Eurohoops BBL", euroCandidates, cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("BasketballSphere EU", () => FetchWordPressInjuryPostsAsync(BasketballSphereWpJsonUrl, "EuroLeague", "BasketballSphere EU", euroCandidates, cancellationToken), fetched, sourceCounts);
        await TryCollectAsync("Basketfaul BSL", () => FetchWordPressInjuryPostsAsync(BasketfaulBslWpJsonUrl, "BSL", "Basketfaul BSL", euroCandidates, cancellationToken), fetched, sourceCounts);

        // Deduplicate fetched entries by (PlayerId, NormalizedInjuryCategory) across overlapping feeds
        var mergedByKey = new Dictionary<(Guid PlayerId, string CategoryKey), (PlayerCandidate Candidate, RawInjuryRecord Record)>();
        var activePlayerIdsByLeague = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in fetched)
        {
            var match = ResolveCandidate(raw.PlayerName, raw.PreferredLeague, raw.TeamName, byNorm, byCompact);
            if (match == null) continue;

            if (!activePlayerIdsByLeague.TryGetValue(raw.PreferredLeague, out var leagueSet))
            {
                leagueSet = new HashSet<Guid>();
                activePlayerIdsByLeague[raw.PreferredLeague] = leagueSet;
            }
            leagueSet.Add(match.Id);

            string catKey = NormalizeInjuryKey(raw.InjuryType);
            var key = (match.Id, catKey);
            if (!mergedByKey.TryGetValue(key, out var existing))
            {
                mergedByKey[key] = (match, raw);
            }
            else
            {
                var betterNotes = (raw.Notes?.Length ?? 0) > (existing.Record.Notes?.Length ?? 0)
                    ? raw.Notes
                    : existing.Record.Notes;
                var betterType = raw.InjuryType != "Undisclosed" && existing.Record.InjuryType == "Undisclosed"
                    ? raw.InjuryType
                    : existing.Record.InjuryType;
                var betterDate = raw.ReportedAt > existing.Record.ReportedAt ? raw.ReportedAt : existing.Record.ReportedAt;
                var combinedSource = existing.Record.Source.Contains(raw.Source, StringComparison.OrdinalIgnoreCase)
                    ? existing.Record.Source
                    : $"{existing.Record.Source} / {raw.Source}";

                mergedByKey[key] = (match, existing.Record with
                {
                    InjuryType = betterType,
                    Notes = betterNotes,
                    ReportedAt = betterDate,
                    Source = combinedSource
                });
            }
        }

        var existingCurrentSeason = await _dbContext.PlayerInjuries
            .Where(i => i.Season == CurrentActiveSeason)
            .ToListAsync(cancellationToken);

        var existingByPlayer = existingCurrentSeason
            .GroupBy(i => i.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        int inserted = 0;
        int updated = 0;
        int recoveredMarked = 0;
        var sampleMatched = new List<SyncedInjuryEntryDto>();

        foreach (var entry in mergedByKey.Values)
        {
            var candidate = entry.Candidate;
            var rec = entry.Record;

            PlayerInjury? existingRow = null;
            if (existingByPlayer.TryGetValue(candidate.Id, out var playerRows))
            {
                existingRow = playerRows.FirstOrDefault(r =>
                    NormalizeInjuryKey(r.InjuryType) == NormalizeInjuryKey(rec.InjuryType) ||
                    (r.IsActive && NormalizeInjuryKey(r.InjuryType) == "undisclosed"));
            }

            if (existingRow != null)
            {
                existingRow.UpdateReport(rec.Status, rec.InjuryType, rec.ReportedAt, rec.Source, rec.Notes);
                updated++;
            }
            else
            {
                var created = new PlayerInjury(
                    candidate.Id,
                    CurrentActiveSeason,
                    rec.InjuryType,
                    rec.Status,
                    rec.ReportedAt,
                    gamesMissed: 0,
                    source: rec.Source,
                    notes: rec.Notes);
                _dbContext.PlayerInjuries.Add(created);
                inserted++;
            }

            if (sampleMatched.Count < 30)
            {
                sampleMatched.Add(new SyncedInjuryEntryDto(
                    candidate.FullName,
                    rec.PreferredLeague,
                    rec.InjuryType,
                    PlayerInjury.NormalizeStatus(rec.Status),
                    rec.Source,
                    rec.Notes));
            }
        }

        // Reconcile previously active 2027 entries that cleared off reliable primary feeds
        bool nbaReliable = sourceCounts.GetValueOrDefault("ESPN NBA") >= 20 || sourceCounts.GetValueOrDefault("BRef NBA") >= 20;
        bool euroReliable = sourceCounts.GetValueOrDefault("Rotowire EuroLeague") >= 10;
        bool ncaaReliable = sourceCounts.GetValueOrDefault("Covers NCAAB") >= 50;

        if (nbaReliable || euroReliable || ncaaReliable)
        {
            var nbaActiveSet = activePlayerIdsByLeague.GetValueOrDefault("NBA") ?? new HashSet<Guid>();
            var euroActiveSet = activePlayerIdsByLeague.GetValueOrDefault("EuroLeague") ?? new HashSet<Guid>();
            var ncaaActiveSet = activePlayerIdsByLeague.GetValueOrDefault("NCAAD1") ?? new HashSet<Guid>();

            foreach (var row in existingCurrentSeason.Where(r => r.IsActive && !string.Equals(r.Source, "Historical Seed", StringComparison.OrdinalIgnoreCase)))
            {
                bool fromNbaFeed = row.Source?.Contains("NBA", StringComparison.OrdinalIgnoreCase) == true;
                bool fromEuroFeed = row.Source?.Contains("EuroLeague", StringComparison.OrdinalIgnoreCase) == true;
                bool fromNcaaFeed = row.Source?.Contains("NCAA", StringComparison.OrdinalIgnoreCase) == true;

                if ((fromNbaFeed && nbaReliable && !nbaActiveSet.Contains(row.PlayerId)) ||
                    (fromEuroFeed && euroReliable && !euroActiveSet.Contains(row.PlayerId)) ||
                    (fromNcaaFeed && ncaaReliable && !ncaaActiveSet.Contains(row.PlayerId)))
                {
                    row.MarkRecovered(DateTime.UtcNow, row.Source);
                    recoveredMarked++;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (inserted + updated + recoveredMarked > 0)
        {
            _cacheState?.BumpVersion();
        }

        _logger.LogInformation(
            "Injury sync completed: {TotalFetched} raw records -> {Matched} matched players ({Inserted} inserted, {Updated} updated, {Recovered} recovered).",
            fetched.Count, mergedByKey.Values.Select(v => v.Candidate.Id).Distinct().Count(), inserted, updated, recoveredMarked);

        return new InjurySyncSummaryDto(
            TotalFetched: fetched.Count,
            MatchedPlayers: mergedByKey.Values.Select(v => v.Candidate.Id).Distinct().Count(),
            InsertedCount: inserted,
            UpdatedCount: updated,
            RecoveredMarkedCount: recoveredMarked,
            SourceCounts: sourceCounts,
            SampleMatched: sampleMatched);
    }

    private async Task TryCollectAsync(
        string sourceName,
        Func<Task<List<RawInjuryRecord>>> fetcher,
        List<RawInjuryRecord> collector,
        Dictionary<string, int> counts)
    {
        try
        {
            var items = await fetcher();
            counts[sourceName] = items.Count;
            collector.AddRange(items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Injury feed '{Source}' failed cleanly and will be skipped.", sourceName);
            counts[sourceName] = 0;
        }
    }

    private async Task<List<RawInjuryRecord>> FetchEspnNbaAsync(CancellationToken ct)
    {
        var results = new List<RawInjuryRecord>();
        using var req = new HttpRequestMessage(HttpMethod.Get, EspnNbaInjuriesUrl);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");

        var resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return results;

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("injuries", out var teams) || teams.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var team in teams.EnumerateArray())
        {
            string? teamName = team.TryGetProperty("displayName", out var tn) ? tn.GetString() : null;
            if (!team.TryGetProperty("injuries", out var items) || items.ValueKind != JsonValueKind.Array) continue;

            foreach (var item in items.EnumerateArray())
            {
                string? playerName = null;
                if (item.TryGetProperty("athlete", out var ath))
                {
                    playerName = ath.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                }
                if (string.IsNullOrWhiteSpace(playerName)) continue;

                string status = item.TryGetProperty("status", out var st) ? (st.GetString() ?? "Out") : "Out";
                string? shortComment = item.TryGetProperty("shortComment", out var sc) ? sc.GetString() : null;
                string? longComment = item.TryGetProperty("longComment", out var lc) ? lc.GetString() : null;
                string? detailType = null;
                if (item.TryGetProperty("details", out var det) && det.TryGetProperty("type", out var dtProp))
                {
                    detailType = dtProp.GetString();
                }

                string injuryType = !string.IsNullOrWhiteSpace(detailType)
                    ? detailType
                    : ExtractInjuryBodyPart(shortComment ?? longComment);

                DateTime reportedAt = DateTime.UtcNow;
                if (item.TryGetProperty("date", out var dProp) && DateTime.TryParse(dProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDt))
                {
                    reportedAt = parsedDt;
                }

                results.Add(new RawInjuryRecord(
                    playerName,
                    "NBA",
                    teamName,
                    injuryType,
                    status,
                    reportedAt,
                    "ESPN NBA",
                    !string.IsNullOrWhiteSpace(shortComment) ? shortComment : longComment));
            }
        }

        return results;
    }

    private async Task<List<RawInjuryRecord>> FetchBRefNbaAsync(CancellationToken ct)
    {
        var results = new List<RawInjuryRecord>();
        using var req = new HttpRequestMessage(HttpMethod.Get, BRefNbaInjuriesUrl);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");

        var resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return results;

        var html = await resp.Content.ReadAsStringAsync(ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode.SelectNodes("//table[@id='injuries']/tbody/tr");
        if (rows == null) return results;

        foreach (var row in rows)
        {
            var playerName = WebUtility.HtmlDecode(row.SelectSingleNode(".//th[@data-stat='player']")?.InnerText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(playerName)) continue;

            var teamName = WebUtility.HtmlDecode(row.SelectSingleNode(".//td[@data-stat='team_name']")?.InnerText ?? "").Trim();
            var dateStr = WebUtility.HtmlDecode(row.SelectSingleNode(".//td[@data-stat='date_update']")?.InnerText ?? "").Trim();
            var note = WebUtility.HtmlDecode(row.SelectSingleNode(".//td[@data-stat='note']")?.InnerText ?? "").Trim();

            DateTime reportedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(dateStr) && DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            {
                reportedAt = dt;
            }

            string status = "Out";
            if (!string.IsNullOrWhiteSpace(note))
            {
                if (note.Contains("Day To Day", StringComparison.OrdinalIgnoreCase) || note.Contains("Day-To-Day", StringComparison.OrdinalIgnoreCase))
                    status = "Day-To-Day";
                else if (note.Contains("Out For Season", StringComparison.OrdinalIgnoreCase))
                    status = "Out (Long-Term)";
                else if (note.Contains("Questionable", StringComparison.OrdinalIgnoreCase))
                    status = "Questionable";
            }

            string injuryType = ExtractInjuryBodyPart(note);
            results.Add(new RawInjuryRecord(playerName, "NBA", teamName, injuryType, status, reportedAt, "BRef NBA", note));
        }

        return results;
    }

    private async Task<List<RawInjuryRecord>> FetchRotowireAsync(string url, string league, string sourceName, CancellationToken ct)
    {
        var results = new List<RawInjuryRecord>();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");

        var resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return results;

        var json = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('[')) return results;

        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string? playerName = item.TryGetProperty("player", out var p) ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(playerName)) continue;

            string? team = item.TryGetProperty("team", out var t) ? t.GetString() : null;
            string injury = item.TryGetProperty("injury", out var inj) ? (inj.GetString() ?? "Undisclosed") : "Undisclosed";
            string status = item.TryGetProperty("status", out var st) ? (st.GetString() ?? "Out") : "Out";

            string? notes = $"{injury} — {status}";
            results.Add(new RawInjuryRecord(playerName, league, team, injury, status, DateTime.UtcNow, sourceName, notes));
        }

        return results;
    }

    private async Task<List<RawInjuryRecord>> FetchCbsNbaAsync(CancellationToken ct)
    {
        var results = new List<RawInjuryRecord>();
        using var req = new HttpRequestMessage(HttpMethod.Get, CbsNbaInjuriesUrl);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");

        var resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return results;

        var html = await resp.Content.ReadAsStringAsync(ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode.SelectNodes("//tr[contains(@class, 'TableBase-bodyTr')]");
        if (rows == null) return results;

        foreach (var row in rows)
        {
            var playerName = WebUtility.HtmlDecode(row.SelectSingleNode(".//*[contains(@class, 'CellPlayerName--long')]//a")?.InnerText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(playerName)) continue;

            var cells = row.SelectNodes("./td");
            if (cells == null || cells.Count < 4) continue;

            string updated = WebUtility.HtmlDecode(cells[2].InnerText).Trim();
            string injury = WebUtility.HtmlDecode(cells[3].InnerText).Trim();
            string statusNote = cells.Count >= 5 ? WebUtility.HtmlDecode(cells[4].InnerText).Trim() : "Out";

            string injuryType = string.IsNullOrWhiteSpace(injury) ? "Undisclosed" : injury;
            string status = PlayerInjury.NormalizeStatus(statusNote);
            results.Add(new RawInjuryRecord(playerName, "NBA", null, injuryType, status, DateTime.UtcNow, "CBS NBA", $"{injury} ({statusNote}; {updated})"));
        }

        return results;
    }

    private async Task<List<RawInjuryRecord>> FetchCoversInjuriesAsync(string url, string league, string sourceName, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");

        var resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return new List<RawInjuryRecord>();

        var html = await resp.Content.ReadAsStringAsync(ct);
        return ParseCoversInjuriesHtml(html, league, sourceName, DateTime.UtcNow);
    }

    internal static List<RawInjuryRecord> ParseCoversInjuriesHtml(
        string html,
        string league,
        string sourceName,
        DateTime? utcNowOverride = null)
    {
        var results = new List<RawInjuryRecord>();
        if (string.IsNullOrWhiteSpace(html)) return results;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var nodes = doc.DocumentNode.SelectNodes("//*[contains(@class,'covers-CoversSeasonInjuries-teamNameColumn')] | //tr[.//a[contains(@class,'player-link')]]");
        if (nodes == null) return results;

        DateTime referenceNow = utcNowOverride ?? DateTime.UtcNow;
        string? currentTeam = null;

        foreach (var node in nodes)
        {
            if (node.Name != "tr")
            {
                string rawTeam = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(node.InnerHtml ?? "", "<[^>]+>", " "), @"\s+", " ")).Trim();
                if (!string.IsNullOrWhiteSpace(rawTeam))
                {
                    currentTeam = rawTeam;
                }
                continue;
            }

            var link = node.SelectSingleNode(".//a[contains(@class,'player-link')]");
            if (link == null) continue;

            string href = link.GetAttributeValue("href", "");
            string innerName = WebUtility.HtmlDecode(Regex.Replace(link.InnerText ?? "", @"\s+", " ")).Trim();
            string playerName = ExtractCoversPlayerName(href, innerName);
            if (string.IsNullOrWhiteSpace(playerName)) continue;

            var cells = node.SelectNodes("./td");
            if (cells == null || cells.Count < 3) continue;

            string statusHeader = WebUtility.HtmlDecode(Regex.Replace(cells[2].SelectSingleNode(".//b | .//strong")?.InnerText ?? "", @"\s+", " ")).Trim();
            string cellText = WebUtility.HtmlDecode(Regex.Replace(cells[2].InnerText ?? "", @"\s+", " ")).Trim();

            string status = "Out";
            string injuryType = "Undisclosed";

            if (!string.IsNullOrWhiteSpace(statusHeader))
            {
                var parts = statusHeader.Split(" - ", 2, StringSplitOptions.TrimEntries);
                status = PlayerInjury.NormalizeStatus(parts[0]);
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    injuryType = parts[1].Trim();
                }
            }
            else if (!string.IsNullOrWhiteSpace(cellText))
            {
                status = PlayerInjury.NormalizeStatus(cellText);
                injuryType = ExtractInjuryBodyPart(cellText);
            }

            DateTime reportedAt = ParseCoversMonthDayDate(cellText, referenceNow);

            string? notes = null;
            var nextRow = node.SelectSingleNode("./following-sibling::tr[1]");
            if (nextRow != null && nextRow.GetAttributeValue("class", "").Contains("collapse", StringComparison.OrdinalIgnoreCase))
            {
                string copy = WebUtility.HtmlDecode(Regex.Replace(
                    nextRow.SelectSingleNode(".//*[contains(@class,'covers-CoversMatchups-injuryCopy')]")?.InnerText ?? "",
                    @"\s+",
                    " ")).Trim();
                if (!string.IsNullOrWhiteSpace(copy))
                {
                    notes = copy;
                }
            }

            results.Add(new RawInjuryRecord(playerName, league, currentTeam, injuryType, status, reportedAt, sourceName, notes ?? statusHeader));
        }

        return results;
    }

    private static string ExtractCoversPlayerName(string href, string fallbackInnerName)
    {
        var slugMatch = CoversPlayerSlugRegex().Match(href ?? "");
        if (slugMatch.Success)
        {
            string slug = slugMatch.Groups[1].Value.Replace('-', ' ').Trim();
            if (!string.IsNullOrWhiteSpace(slug))
            {
                return slug;
            }
        }
        return fallbackInnerName;
    }

    private static DateTime ParseCoversMonthDayDate(string text, DateTime referenceNow)
    {
        var m = CoversMonthDayRegex().Match(text ?? "");
        if (m.Success && DateTime.TryParseExact(
                $"{m.Groups[1].Value} {referenceNow.Year}",
                new[] { "MMM d yyyy", "MMMM d yyyy" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            // Handle year wrap (e.g. querying Nov/Dec report dates in Jan/Feb)
            if (dt > referenceNow.AddDays(45))
            {
                dt = dt.AddYears(-1);
            }
            return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
        }
        return referenceNow;
    }

    private static PlayerCandidate? ResolveCandidate(
        string rawName,
        string preferredLeague,
        string? teamHint,
        Dictionary<string, List<PlayerCandidate>> byNorm,
        Dictionary<string, List<PlayerCandidate>> byCompact)
    {
        var norm = Player.NormalizeName(rawName, string.Empty);
        var compact = StripSuffixAndPunct(norm);

        List<PlayerCandidate>? pool = null;
        if (!byNorm.TryGetValue(norm, out pool) || pool.Count == 0)
        {
            byCompact.TryGetValue(compact, out pool);
        }

        if (pool == null || pool.Count == 0) return null;
        if (pool.Count == 1) return pool[0];

        return pool
            .OrderByDescending(c => c.ActiveLeagues.Contains(preferredLeague) || string.Equals(c.PrimaryLeague, preferredLeague, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(c => !string.IsNullOrWhiteSpace(teamHint) && !string.IsNullOrWhiteSpace(c.TeamName) && teamHint.Contains(c.TeamName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(c => c.ActiveLeagues.Count)
            .First();
    }

    private static string StripSuffixAndPunct(string norm)
    {
        var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t != "jr" && t != "sr" && t != "ii" && t != "iii" && t != "iv")
            .ToArray();
        return string.Concat(tokens);
    }

    private static string NormalizeInjuryKey(string? injury)
    {
        if (string.IsNullOrWhiteSpace(injury)) return "undisclosed";
        var lower = injury.Trim().ToLowerInvariant();
        string[] canonicalParts = { "acl", "achilles", "meniscus", "knee", "ankle", "foot", "toe", "calf", "hamstring", "quad", "groin", "hip", "back", "spine", "shoulder", "elbow", "wrist", "hand", "finger", "thumb", "concussion", "illness", "personal", "rest" };
        foreach (var part in canonicalParts)
        {
            if (lower.Contains(part, StringComparison.Ordinal))
                return part;
        }
        return Regex.Replace(lower, @"[^a-z0-9]", "");
    }

    private async Task<List<RawInjuryRecord>> FetchPiratasAcbParteMedicoAsync(IReadOnlyList<PlayerCandidate> acbCandidates, CancellationToken ct)
    {
        if (acbCandidates.Count == 0) return new List<RawInjuryRecord>();
        using var req = new HttpRequestMessage(HttpMethod.Get, PiratasAcbRssUrl);
        req.Headers.TryAddWithoutValidation("User-Agent", "SmartScoutAnalytics/1.0 (+https://smartscout.local)");
        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var xml = await resp.Content.ReadAsStringAsync(ct);
        return ParseAcbParteMedicoRss(xml, acbCandidates, DateTime.UtcNow);
    }

    internal static List<RawInjuryRecord> ParseAcbParteMedicoRss(string rssXml, IReadOnlyList<PlayerCandidate> acbCandidates, DateTime utcNow)
    {
        var results = new List<RawInjuryRecord>();
        if (string.IsNullOrWhiteSpace(rssXml) || acbCandidates.Count == 0)
            return results;

        XDocument doc;
        try { doc = XDocument.Parse(rssXml); }
        catch { return results; }

        XNamespace contentNs = "http://purl.org/rss/1.0/modules/content/";
        var cutoff = utcNow.AddDays(-14);

        var articles = doc.Descendants("item")
            .Select(it => new
            {
                Title = (string?)it.Element("title") ?? string.Empty,
                PubDate = ParseRssDate((string?)it.Element("pubDate"), utcNow),
                Html = (string?)it.Element(contentNs + "encoded") ?? (string?)it.Element("description") ?? string.Empty
            })
            .Where(a => a.PubDate >= cutoff && IsAcbParteMedicoTitle(a.Title))
            .OrderByDescending(a => a.PubDate)
            .Take(1)
            .ToList();

        var filteredCandidates = acbCandidates
            .Where(c => !string.IsNullOrWhiteSpace(c.NormalizedName) && c.NormalizedName.Contains(' ') && c.NormalizedName.Length >= 6)
            .GroupBy(c => c.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var art in articles)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(art.Html);
            var paragraphs = htmlDoc.DocumentNode.SelectNodes("//p");
            if (paragraphs == null) continue;

            foreach (var p in paragraphs)
            {
                string rawP = CleanText(WebUtility.HtmlDecode(p.InnerText));
                if (!rawP.StartsWith("•", StringComparison.Ordinal) && !rawP.Contains(':', StringComparison.Ordinal))
                    continue;

                string trimmed = rawP.TrimStart('•', ' ');
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 1 || colonIdx > 45) continue;

                string teamName = trimmed[..colonIdx].Trim();
                string body = trimmed[(colonIdx + 1)..].Trim();
                var clauses = Regex.Split(body, @"(?<=[.;])\s+|,\s*(?=mientras que\b|aunque\b|salvo\b|mientras\b)", RegexOptions.IgnoreCase);

                foreach (var clause in clauses)
                {
                    if (string.IsNullOrWhiteSpace(clause)) continue;
                    string normClause = " " + Player.NormalizeName(clause, string.Empty) + " ";
                    string lowerClause = clause.ToLowerInvariant();

                    foreach (var cand in filteredCandidates)
                    {
                        string needle = " " + cand.NormalizedName + " ";
                        int matchIdx = normClause.IndexOf(needle, StringComparison.Ordinal);
                        if (matchIdx < 0) continue;

                        string? status = ClassifyAcbClauseStatus(lowerClause, normClause, cand.NormalizedName);
                        if (status == null) continue;

                        string rawSnippet = ExtractLocalClauseWindow(clause, cand.FullName, cand.NormalizedName);
                        string injuryType = ExtractInjuryBodyPart(rawSnippet);
                        if (injuryType == "Undisclosed")
                            injuryType = ExtractInjuryBodyPart(clause);

                        string note = $"{art.Title} ({teamName}): {clause}";
                        if (note.Length > 340) note = note[..340];

                        results.Add(new RawInjuryRecord(
                            cand.NormalizedName,
                            "ACB",
                            teamName,
                            injuryType,
                            status,
                            art.PubDate,
                            "Piratas ACB",
                            note));
                    }
                }
            }
        }

        return results;
    }

    private async Task<List<RawInjuryRecord>> FetchEurohoopsRssAsync(
        string url,
        string preferredLeague,
        string sourceName,
        IReadOnlyList<PlayerCandidate> euroCandidates,
        CancellationToken ct)
    {
        if (euroCandidates.Count == 0) return new List<RawInjuryRecord>();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "SmartScoutAnalytics/1.0 (+https://smartscout.local)");
        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var xml = await resp.Content.ReadAsStringAsync(ct);

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return new List<RawInjuryRecord>(); }

        var posts = doc.Descendants("item")
            .Select(it => (
                Title: CleanText(WebUtility.HtmlDecode((string?)it.Element("title") ?? string.Empty)),
                Body: StripHtmlToText((string?)it.Element("description") ?? string.Empty),
                PublishedAt: ParseRssDate((string?)it.Element("pubDate"), DateTime.UtcNow)))
            .ToList();

        return ParseEuropeanNewsItems(posts, preferredLeague, sourceName, euroCandidates, DateTime.UtcNow);
    }

    private async Task<List<RawInjuryRecord>> FetchWordPressInjuryPostsAsync(
        string url,
        string preferredLeague,
        string sourceName,
        IReadOnlyList<PlayerCandidate> euroCandidates,
        CancellationToken ct)
    {
        if (euroCandidates.Count == 0) return new List<RawInjuryRecord>();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            return new List<RawInjuryRecord>();

        var posts = new List<(string Title, string Body, DateTime PublishedAt)>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string rawTitle = el.TryGetProperty("title", out var tObj) && tObj.TryGetProperty("rendered", out var tRend)
                ? (tRend.GetString() ?? string.Empty)
                : string.Empty;
            string rawBody = el.TryGetProperty("excerpt", out var eObj) && eObj.TryGetProperty("rendered", out var eRend)
                ? (eRend.GetString() ?? string.Empty)
                : string.Empty;
            DateTime pub = el.TryGetProperty("date", out var dVal) && DateTime.TryParse(dVal.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDt)
                ? parsedDt
                : DateTime.UtcNow;

            posts.Add((StripHtmlToText(rawTitle), StripHtmlToText(rawBody), pub));
        }

        return ParseEuropeanNewsItems(posts, preferredLeague, sourceName, euroCandidates, DateTime.UtcNow);
    }

    internal static List<RawInjuryRecord> ParseEuropeanNewsItems(
        IEnumerable<(string Title, string Body, DateTime PublishedAt)> items,
        string preferredLeague,
        string sourceName,
        IReadOnlyList<PlayerCandidate> euroCandidates,
        DateTime utcNow)
    {
        var results = new List<RawInjuryRecord>();
        var cutoff = utcNow.AddDays(-21);

        var distinctCandidates = euroCandidates
            .Where(c => !string.IsNullOrWhiteSpace(c.NormalizedName) && c.NormalizedName.Contains(' ') && c.NormalizedName.Length >= 6)
            .GroupBy(c => c.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var item in items)
        {
            if (item.PublishedAt < cutoff || string.IsNullOrWhiteSpace(item.Title))
                continue;

            if (!IsInjuryNewsHeadline(item.Title) || IsNoiseNewsHeadline(item.Title))
                continue;

            string normTitle = " " + Player.NormalizeName(item.Title, string.Empty) + " ";
            string firstSentence = item.Body.Split(new[] { ". ", "! ", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? item.Body;
            string normLead = " " + Player.NormalizeName(firstSentence, string.Empty) + " ";
            string status = ClassifyNewsHeadlineStatus(item.Title);

            foreach (var cand in distinctCandidates)
            {
                string fullNeedle = " " + cand.NormalizedName + " ";
                bool matchedInTitle = normTitle.Contains(fullNeedle, StringComparison.Ordinal);

                bool matchedViaSurnameHeadline = false;
                if (!matchedInTitle && normLead.Contains(fullNeedle, StringComparison.Ordinal))
                {
                    var parts = cand.NormalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string lastName = parts.Length >= 2 ? parts[^1] : string.Empty;
                    if (lastName.Length >= 4 && normTitle.Contains(" " + lastName + " ", StringComparison.Ordinal))
                    {
                        matchedViaSurnameHeadline = true;
                    }
                }

                if (!matchedInTitle && !matchedViaSurnameHeadline)
                    continue;

                string localWindow = matchedInTitle
                    ? ExtractLocalClauseWindow(item.Title, cand.FullName, cand.NormalizedName)
                    : ExtractLocalClauseWindow(firstSentence, cand.FullName, cand.NormalizedName);

                string injuryType = ExtractInjuryBodyPart(localWindow);
                if (injuryType == "Undisclosed")
                {
                    injuryType = ExtractInjuryBodyPart($"{item.Title}. {firstSentence}");
                }

                string leagueHint = !string.IsNullOrWhiteSpace(cand.PrimaryLeague) && EuropeanLeagues.Contains(cand.PrimaryLeague)
                    ? cand.PrimaryLeague
                    : preferredLeague;

                string note = string.IsNullOrWhiteSpace(firstSentence) ? item.Title : $"{item.Title} — {firstSentence}";
                if (note.Length > 340) note = note[..340];

                results.Add(new RawInjuryRecord(
                    cand.NormalizedName,
                    leagueHint,
                    cand.TeamName,
                    injuryType,
                    status,
                    item.PublishedAt,
                    sourceName,
                    note));
            }
        }

        return results;
    }

    private static bool IsAcbParteMedicoTitle(string title)
    {
        string norm = Player.NormalizeName(title, string.Empty);
        return norm.Contains("parte medico", StringComparison.Ordinal);
    }

    private static string? ClassifyAcbClauseStatus(string lowerClause, string normClause, string normPlayerName)
    {
        if (normClause.Contains("ya esta recuperado", StringComparison.Ordinal) ||
            normClause.Contains("regresa tras superar", StringComparison.Ordinal) ||
            normClause.Contains("regresa tras su lesion", StringComparison.Ordinal))
        {
            return "Recovered";
        }

        // Ignore optimistic/available phrasing unless explicitly preceded by "salvo X" (everyone except X)
        int playerIdx = normClause.IndexOf(" " + normPlayerName + " ", StringComparison.Ordinal);
        int salvoIdx = normClause.IndexOf(" salvo ", StringComparison.Ordinal);
        if (salvoIdx >= 0 && playerIdx > salvoIdx)
        {
            return "Out";
        }

        if (normClause.Contains("se espera que", StringComparison.Ordinal) ||
            normClause.Contains("podra jugar", StringComparison.Ordinal) ||
            normClause.Contains("podria regresar", StringComparison.Ordinal) ||
            normClause.Contains("podria debutar", StringComparison.Ordinal) ||
            normClause.Contains("da por segura", StringComparison.Ordinal) ||
            normClause.Contains("han podido entrenar", StringComparison.Ordinal) ||
            normClause.Contains("formaran parte de la convocatoria", StringComparison.Ordinal))
        {
            return null;
        }

        if (lowerClause.Contains("duda", StringComparison.Ordinal) ||
            normClause.Contains("en el aire", StringComparison.Ordinal) ||
            normClause.Contains("incognita", StringComparison.Ordinal))
        {
            return "Questionable";
        }

        if (lowerClause.Contains("baja", StringComparison.Ordinal) ||
            lowerClause.Contains("ausencia", StringComparison.Ordinal) ||
            lowerClause.Contains("no estará disponible", StringComparison.Ordinal) ||
            lowerClause.Contains("no estara disponible", StringComparison.Ordinal) ||
            lowerClause.Contains("se perderá", StringComparison.Ordinal) ||
            lowerClause.Contains("se suma", StringComparison.Ordinal) ||
            lowerClause.Contains("se unen", StringComparison.Ordinal) ||
            lowerClause.Contains("sufre", StringComparison.Ordinal) ||
            lowerClause.Contains("lesión", StringComparison.Ordinal) ||
            lowerClause.Contains("lesion", StringComparison.Ordinal) ||
            lowerClause.Contains("esguince", StringComparison.Ordinal) ||
            lowerClause.Contains("rotura", StringComparison.Ordinal) ||
            lowerClause.Contains("subluxación", StringComparison.Ordinal) ||
            lowerClause.Contains("fractura", StringComparison.Ordinal))
        {
            return "Out";
        }

        return null;
    }

    private static bool IsInjuryNewsHeadline(string title)
    {
        return EuropeanInjuryHeadlineRegex().IsMatch(title);
    }

    private static bool IsNoiseNewsHeadline(string title)
    {
        return EuropeanNoiseHeadlineRegex().IsMatch(title);
    }

    private static string ClassifyNewsHeadlineStatus(string title)
    {
        string lower = title.ToLowerInvariant();
        if (lower.Contains("returns from injury", StringComparison.Ordinal) ||
            lower.Contains("cleared to play", StringComparison.Ordinal) ||
            lower.Contains("sakatlıktan çık", StringComparison.Ordinal) ||
            lower.Contains("sakatliktan cik", StringComparison.Ordinal) ||
            lower.Contains("parkelere dön", StringComparison.Ordinal))
        {
            return "Recovered";
        }

        if (lower.Contains("doubtful", StringComparison.Ordinal))
            return "Doubtful";
        if (lower.Contains("questionable", StringComparison.Ordinal))
            return "Questionable";
        if (lower.Contains("scare", StringComparison.Ordinal) || lower.Contains("day-to-day", StringComparison.Ordinal))
            return "Day-To-Day";

        return "Out";
    }

    private static string ExtractLocalClauseWindow(string text, string fullName, string normName)
    {
        var last = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? fullName;
        foreach (var segment in Regex.Split(text, @"[,;:]|\band\b|\bmientras que\b", RegexOptions.IgnoreCase))
        {
            string normSeg = " " + Player.NormalizeName(segment, string.Empty) + " ";
            if (normSeg.Contains(" " + normName + " ", StringComparison.Ordinal) ||
                segment.Contains(fullName, StringComparison.OrdinalIgnoreCase) ||
                (last.Length >= 4 && segment.Contains(last, StringComparison.OrdinalIgnoreCase)))
            {
                return segment.Trim();
            }
        }

        int rawIdx = text.IndexOf(fullName, StringComparison.OrdinalIgnoreCase);
        if (rawIdx < 0)
            rawIdx = text.IndexOf(last, StringComparison.OrdinalIgnoreCase);
        if (rawIdx < 0) return text;

        int start = Math.Max(0, rawIdx - 25);
        int len = Math.Min(text.Length - start, fullName.Length + 60);
        return text.Substring(start, len);
    }

    private static string CleanText(string? input) =>
        string.IsNullOrWhiteSpace(input) ? string.Empty : Regex.Replace(input, @"\s+", " ").Trim();

    private static string StripHtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        string noTags = Regex.Replace(html, @"<[^>]+>", " ");
        return CleanText(WebUtility.HtmlDecode(noTags));
    }

    private static DateTime ParseRssDate(string? rawDate, DateTime fallbackUtc)
    {
        if (!string.IsNullOrWhiteSpace(rawDate) &&
            DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto))
        {
            return dto.UtcDateTime;
        }
        return fallbackUtc;
    }

    private static string ExtractInjuryBodyPart(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return "Undisclosed";
        var parenMatch = BodyPartParenRegex().Match(note);
        if (parenMatch.Success)
        {
            string parenText = parenMatch.Groups[1].Value.Trim();
            string mappedParen = MapKnownBodyPart(parenText);
            if (mappedParen != "Undisclosed")
                return mappedParen;

            // Avoid turning club/date parentheses like "(Fenerbahce)" or "(Thu, Nov 12)" into injury types
            if (parenText.Length <= 22 && !parenText.Contains(',') && !Regex.IsMatch(parenText, @"\d"))
                return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parenText.ToLowerInvariant());
        }

        return MapKnownBodyPart(note);
    }

    private static string MapKnownBodyPart(string text)
    {
        string lower = text.ToLowerInvariant();
        (string Key, string Label)[] known =
        {
            ("acl", "ACL / Knee"),
            ("cruzado", "ACL / Knee"),
            ("çapraz bağ", "ACL / Knee"),
            ("capraz bag", "ACL / Knee"),
            ("achilles", "Achilles"),
            ("aquiles", "Achilles"),
            ("aşil", "Achilles"),
            ("meniscus", "Knee Meniscus"),
            ("menisco", "Knee Meniscus"),
            ("menisküs", "Knee Meniscus"),
            ("meniskus", "Knee Meniscus"),
            ("knee", "Knee"),
            ("rodilla", "Knee"),
            ("diz", "Knee"),
            ("ayak bile", "Ankle"),
            ("ankle", "Ankle"),
            ("tobillo", "Ankle"),
            ("great toe", "Toe"),
            ("toe", "Toe"),
            ("foot", "Foot"),
            ("pie ", "Foot"),
            ("calf", "Calf"),
            ("gemelo", "Calf"),
            ("hamstring", "Hamstring"),
            ("isquiotibiales", "Hamstring"),
            ("arka adale", "Hamstring"),
            ("quad", "Quad"),
            ("cuádriceps", "Quad"),
            ("groin", "Groin"),
            ("kasık", "Groin"),
            ("hip", "Hip"),
            ("cadera", "Hip"),
            ("abdominal", "Abdominal"),
            ("oblicuo", "Abdominal"),
            ("karın", "Abdominal"),
            ("back", "Back"),
            ("espalda", "Back"),
            ("lumbar", "Back"),
            ("shoulder", "Shoulder"),
            ("hombro", "Shoulder"),
            ("omuz", "Shoulder"),
            ("wrist", "Wrist"),
            ("muñeca", "Wrist"),
            ("hand", "Hand"),
            ("mano", "Hand"),
            ("finger", "Finger"),
            ("dedo", "Finger"),
            ("parmak", "Finger"),
            ("thumb", "Thumb"),
            ("concussion", "Concussion"),
            ("conmoción", "Concussion"),
            ("illness", "Illness"),
            ("vírico", "Illness"),
            ("virico", "Illness"),
            ("gripe", "Illness"),
            ("personal", "Personal"),
            ("muscular", "Muscle"),
            ("muscle", "Muscle"),
            ("adale", "Muscle"),
            ("esguince", "Sprain"),
            ("fractur", "Fracture"),
            ("kırık", "Fracture"),
            ("surgery", "Surgery"),
            ("operasyon", "Surgery"),
            ("ameliyat", "Surgery")
        };

        foreach (var item in known)
        {
            if (lower.Contains(item.Key, StringComparison.Ordinal))
                return item.Label;
        }

        return "Undisclosed";
    }

    [GeneratedRegex(@"\(([A-Za-z0-9\s/-]{2,30})\)", RegexOptions.IgnoreCase)]
    private static partial Regex BodyPartParenRegex();

    [GeneratedRegex(@"/players/\d+/([^/?#""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex CoversPlayerSlugRegex();

    [GeneratedRegex(@"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun),?\s+([A-Za-z]{3,9}\s+\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex CoversMonthDayRegex();

    [GeneratedRegex(@"\b(injur\w*|surgery|sidelined|meniscus|dislocated|acl|achilles|sprain|fractur\w*|ruptur\w*|tear|torn|out for|ruled out|doubtful|questionable|sakat\w*|operasyon|ameliyat|menisk\w*|kırık|yırtık)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EuropeanInjuryHeadlineRegex();

    [GeneratedRegex(@"(prediction\s*&\s*odds|player props|acca of|betlabel|preliminary roster|wins for|signs with|officially joins|leaves .* and joins|evaluates|takas iddiası|ayakkabı)", RegexOptions.IgnoreCase)]
    private static partial Regex EuropeanNoiseHeadlineRegex();
}
