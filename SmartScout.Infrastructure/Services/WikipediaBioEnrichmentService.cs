using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Services.Scraping;

namespace SmartScout.Infrastructure.Services;

public partial class WikipediaBioEnrichmentService : IPlayerBioEnrichmentService
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WikipediaBioEnrichmentService> _logger;

    public WikipediaBioEnrichmentService(
        ISmartScoutDbContext dbContext,
        HttpClient httpClient,
        ILogger<WikipediaBioEnrichmentService> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _logger = logger;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SmartScoutAnalytics/1.0 (contact@smartscout.io; automated scouting bot)");
        }
    }

    public async Task<BioEnrichmentSummaryDto> EnrichPlayerBiosAsync(
        string? league = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        int safeLimit = Math.Clamp(limit, 1, 2000);

        var query = _dbContext.Players
            .Include(p => p.SeasonStats)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(league) && !league.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.League == league);
        }

        var targetPlayers = await query
            .Where(p => p.BirthDate == null || p.HeightCm == null || p.WeightKg == null || p.PrimaryPosition == null || p.Nationality == null || p.Nationality == "INTL")
            .OrderByDescending(p => p.SeasonStats.Any(s => s.Season >= 2026 && s.GamesPlayed >= 3) ? 1 : 0)
            .ThenByDescending(p => p.League != "NCAAD1" ? 1 : 0)
            .ThenByDescending(p => p.BirthDate == null ? 1 : 0)
            .ThenByDescending(p => (p.Nationality == null || p.Nationality == "INTL") ? 1 : 0)
            .ThenByDescending(p => p.SeasonStats.Max(s => (int?)s.Season) ?? 0)
            .ThenByDescending(p => p.SeasonStats.Count)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        if (!targetPlayers.Any())
        {
            return new BioEnrichmentSummaryDto(0, 0, 0, new List<EnrichedPlayerEntryDto>());
        }

        var enrichedList = await EnrichPlayerBatchListAsync(targetPlayers, cancellationToken);
        int failedCount = Math.Max(0, targetPlayers.Count - enrichedList.Count);

        return new BioEnrichmentSummaryDto(targetPlayers.Count, enrichedList.Count, failedCount, enrichedList);
    }

    public async Task<BioEnrichmentSummaryDto> EnrichAllDatabasePlayersAsync(
        int concurrency = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Multi-Source Automated Player Bio Enrichment...");

        var allNeedingEnrichment = await _dbContext.Players
            .Include(p => p.SeasonStats)
            .Where(p => p.BirthDate == null || p.HeightCm == null || p.WeightKg == null || p.PrimaryPosition == null || p.Nationality == null || p.Nationality == "INTL")
            .OrderByDescending(p => p.SeasonStats.Any(s => s.Season >= 2026 && s.GamesPlayed >= 5) ? 1 : 0)
            .ThenByDescending(p => p.BirthDate == null ? 1 : 0)
            .ThenByDescending(p => p.SeasonStats.Max(s => (int?)s.Season) ?? 0)
            .ThenByDescending(p => p.SeasonStats.Count)
            .ToListAsync(cancellationToken);

        int totalFound = allNeedingEnrichment.Count;
        if (totalFound == 0)
        {
            return new BioEnrichmentSummaryDto(0, 0, 0, new List<EnrichedPlayerEntryDto>());
        }

        // Perform instant local unit normalizations first (Height string -> cm, Weight lbs -> kg, Position -> Primary/Secondary)
        int internalNormalizedCount = 0;
        foreach (var p in allNeedingEnrichment)
        {
            bool changed = false;
            if (p.HeightCm == null && !string.IsNullOrWhiteSpace(p.Height))
            {
                var (cm, _) = ExtractHeight($"| height = {p.Height}");
                if (cm.HasValue) { p.UpdateDetails(p.FirstName, p.LastName, p.Position, p.Height, p.WeightPounds, p.TeamName, p.League, p.Nationality, p.BirthDate, cm, p.WeightKg, p.WingspanCm, p.DraftYear, p.DraftPick); changed = true; }
            }
            if (p.WeightKg == null && p.WeightPounds.HasValue && p.WeightPounds.Value > 0)
            {
                int kg = (int)Math.Round(p.WeightPounds.Value * 0.45359237);
                p.UpdateDetails(p.FirstName, p.LastName, p.Position, p.Height, p.WeightPounds, p.TeamName, p.League, p.Nationality, p.BirthDate, p.HeightCm, kg, p.WingspanCm, p.DraftYear, p.DraftPick);
                changed = true;
            }
            if (p.PrimaryPosition == null && !string.IsNullOrWhiteSpace(p.Position))
            {
                string? pos = ExtractPosition($"| position = {p.Position}");
                if (!string.IsNullOrWhiteSpace(pos)) { p.UpdateDetails(p.FirstName, p.LastName, pos, p.Height, p.WeightPounds, p.TeamName, p.League, p.Nationality, p.BirthDate, p.HeightCm, p.WeightKg, p.WingspanCm, p.DraftYear, p.DraftPick); changed = true; }
            }
            if (changed) internalNormalizedCount++;
        }

        if (internalNormalizedCount > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var playersNeedingRemote = allNeedingEnrichment
            .Where(p => p.BirthDate == null || p.HeightCm == null || p.WeightKg == null || p.Nationality == null || p.Nationality == "INTL")
            .ToList();

        var enrichedList = await EnrichPlayerBatchListAsync(playersNeedingRemote, cancellationToken);
        int failed = Math.Max(0, totalFound - enrichedList.Count);
        return new BioEnrichmentSummaryDto(totalFound, enrichedList.Count, failed, enrichedList);
    }

    private async Task<List<EnrichedPlayerEntryDto>> EnrichPlayerBatchListAsync(List<Player> targetPlayers, CancellationToken cancellationToken)
    {
        var enrichedById = new Dictionary<Guid, EnrichedPlayerEntryDto>();
        void TrackEnriched(Player p, EnrichedPlayerEntryDto? dto)
        {
            if (dto != null) enrichedById[p.Id] = dto;
        }

        // Pass 1: Official EuroLeague & EuroCup Feeds v2 API (covers EL, EC + domestic league crossovers)
        var (euroBioIndex, euroBioByDobLast) = await BuildEuroLeagueFeedBioIndexAsync(cancellationToken);
        if (euroBioIndex.Count > 0)
        {
            foreach (var p in targetPlayers.Where(NeedsRemoteBio))
            {
                var key = NormalizeLookupKey(p);
                EuroFeedBio? eb = null;
                if (!euroBioIndex.TryGetValue(key, out eb))
                {
                    var parts = (p.FullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length >= 3)
                    {
                        var firstLastKey = Player.NormalizeName(parts[0], parts[^1]);
                        if (euroBioIndex.TryGetValue(firstLastKey, out var flMatch) &&
                            (!p.BirthDate.HasValue || !flMatch.BirthDate.HasValue || p.BirthDate.Value.Year == flMatch.BirthDate.Value.Year))
                        {
                            eb = flMatch;
                        }
                    }
                    if (eb == null && p.BirthDate.HasValue && parts.Length >= 1)
                    {
                        string lastKey = $"{Player.NormalizeName(parts[^1], string.Empty)}|{p.BirthDate.Value:yyyy-MM-dd}";
                        euroBioByDobLast.TryGetValue(lastKey, out eb);
                    }
                }

                if (eb != null)
                {
                    var dto = TryApplyBioUpdate(p, eb.BirthDate, eb.HeightCm, eb.WeightKg, null, null, eb.Nationality, null, null);
                    TrackEnriched(p, dto);
                }
            }
            if (enrichedById.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // Pass 2: English Wikipedia batched wikitext lookup
        var wikiCandidates = targetPlayers.Where(NeedsRemoteBio).ToList();
        const int batchSize = 40;
        for (int i = 0; i < wikiCandidates.Count; i += batchSize)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var batch = wikiCandidates.Skip(i).Take(batchSize).ToList();
            var batchEnriched = await ProcessWikiBatchAsync(batch, cancellationToken);
            foreach (var item in batchEnriched)
            {
                var owner = batch.FirstOrDefault(p => p.FullName == item.FullName && p.League == item.League);
                if (owner != null) enrichedById[owner.Id] = item;
            }
            if (batchEnriched.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // Pass 3: Wikidata structured claims (P569 DOB + P27 citizenship + P106=Q3665646 basketball player) + Multi-lang Wikipedia sitelinks
        var wikidataCandidates = targetPlayers
            .Where(p => p.BirthDate == null || (NeedsNationality(p) && !string.Equals(p.League, "NCAAD1", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        const int wikidataChunkSize = 120;
        for (int i = 0; i < wikidataCandidates.Count; i += wikidataChunkSize)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var chunk = wikidataCandidates.Skip(i).Take(wikidataChunkSize).ToList();
            var wdEnriched = await EnrichFromWikidataAsync(chunk, cancellationToken);
            foreach (var (p, dto) in wdEnriched) TrackEnriched(p, dto);
            if (wdEnriched.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // Pass 4: Basketball-Reference direct player bio pages for remaining missing DOB or pro Nationality across BRef leagues
        var brefCandidates = targetPlayers
            .Where(p => p.BirthDate == null || (NeedsNationality(p) && !string.Equals(p.League, "NCAAD1", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (brefCandidates.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var brefEnriched = await EnrichFromBasketballReferenceAsync(brefCandidates, cancellationToken);
            foreach (var (p, dto) in brefEnriched) TrackEnriched(p, dto);
            if (brefEnriched.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // Pass 5: Multi-language league-matched Wikipedia fallback for any remaining non-NCAA players missing DOB or Nationality
        var multiWikiCandidates = targetPlayers.Where(p => NeedsDobOrNationality(p) && !string.Equals(p.League, "NCAAD1", StringComparison.OrdinalIgnoreCase)).ToList();
        const int multiWikiChunkSize = 80;
        for (int i = 0; i < multiWikiCandidates.Count; i += multiWikiChunkSize)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var chunk = multiWikiCandidates.Skip(i).Take(multiWikiChunkSize).ToList();
            var mwEnriched = await EnrichFromMultiLanguageWikipediaAsync(chunk, cancellationToken);
            foreach (var (p, dto) in mwEnriched) TrackEnriched(p, dto);
            if (mwEnriched.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return enrichedById.Values.ToList();
    }

    private static bool NeedsNationality(Player p) =>
        string.IsNullOrWhiteSpace(p.Nationality) || p.Nationality == "INTL";

    private static bool NeedsDobOrNationality(Player p) =>
        p.BirthDate == null || NeedsNationality(p);

    private static bool NeedsRemoteBio(Player p) =>
        p.BirthDate == null || p.HeightCm == null || p.WeightKg == null || NeedsNationality(p);

    private static string NormalizeLookupKey(Player p) =>
        Player.NormalizeName(p.FirstName, p.LastName);

    private async Task<List<EnrichedPlayerEntryDto>> ProcessWikiBatchAsync(List<Player> batch, CancellationToken cancellationToken)
    {
        var enriched = new List<EnrichedPlayerEntryDto>();
        var titleToPlayerMap = new Dictionary<string, Player>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in batch)
        {
            string cleanTitle = CleanWikiTitle(p.FullName);
            if (!string.IsNullOrWhiteSpace(cleanTitle) && !titleToPlayerMap.ContainsKey(cleanTitle))
            {
                titleToPlayerMap[cleanTitle] = p;
            }
        }

        if (!titleToPlayerMap.Any()) return enriched;

        string titlesParam = string.Join("|", titleToPlayerMap.Keys.Take(40));
        string url = $"https://en.wikipedia.org/w/api.php?action=query&redirects=1&prop=revisions&rvprop=content&rvsection=0&titles={Uri.EscapeDataString(titlesParam)}&format=json";

        try
        {
            // Respectful delay between batches to strictly comply with Wikipedia API rate limits
            await Task.Delay(150, cancellationToken);

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Wikipedia API 429 rate limit reached. Pausing for 2 seconds...");
                    await Task.Delay(2000, cancellationToken);
                }
                return enriched;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("query", out var queryElem) ||
                !queryElem.TryGetProperty("pages", out var pagesElem))
            {
                return enriched;
            }

            // Build reverse maps for Wikipedia title normalizations and redirects (e.g. "Iffe_Lundberg" -> "Gabriel Lundberg")
            var reverseMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            void AddReverseEdge(string to, string from)
            {
                if (!reverseMap.TryGetValue(to, out var list))
                {
                    list = new List<string>();
                    reverseMap[to] = list;
                }
                list.Add(from);
            }

            if (queryElem.TryGetProperty("normalized", out var normArr))
            {
                foreach (var item in normArr.EnumerateArray())
                {
                    AddReverseEdge(item.GetProperty("to").GetString() ?? "", item.GetProperty("from").GetString() ?? "");
                }
            }
            if (queryElem.TryGetProperty("redirects", out var redirArr))
            {
                foreach (var item in redirArr.EnumerateArray())
                {
                    AddReverseEdge(item.GetProperty("to").GetString() ?? "", item.GetProperty("from").GetString() ?? "");
                }
            }

            foreach (var pageProp in pagesElem.EnumerateObject())
            {
                var page = pageProp.Value;
                if (page.TryGetProperty("missing", out _)) continue;

                string pageTitle = page.GetProperty("title").GetString() ?? "";
                string cleanPageName = pageTitle.Replace("(basketball)", "").Replace("(basketball player)", "").Trim();
                string normPage = Player.NormalizeName(cleanPageName, "");

                // Trace any redirected/normalized original request titles back to the target player
                var tracedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pageTitle, pageTitle.Replace(' ', '_') };
                var queue = new Queue<string>(tracedTitles);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    if (reverseMap.TryGetValue(cur, out var sources))
                    {
                        foreach (var src in sources)
                        {
                            if (tracedTitles.Add(src)) queue.Enqueue(src);
                            var srcUnder = src.Replace(' ', '_');
                            if (tracedTitles.Add(srcUnder)) queue.Enqueue(srcUnder);
                        }
                    }
                }

                Player? player = null;
                foreach (var t in tracedTitles)
                {
                    if (titleToPlayerMap.TryGetValue(t, out player)) break;
                }

                // Fallback: match player by normalized name
                player ??= batch.FirstOrDefault(p =>
                    string.Equals(p.NormalizedName, normPage, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Player.NormalizeName(p.FirstName, p.LastName), normPage, StringComparison.OrdinalIgnoreCase) ||
                    normPage.Contains(p.NormalizedName, StringComparison.OrdinalIgnoreCase) ||
                    p.NormalizedName.Contains(normPage, StringComparison.OrdinalIgnoreCase));

                if (player == null) continue;

                if (page.TryGetProperty("revisions", out var revs) && revs.GetArrayLength() > 0)
                {
                    var firstRev = revs[0];
                    string wikitext = firstRev.GetProperty("*").GetString() ?? "";

                    // Guardrail: Ensure article is specifically a basketball player biography (prevents R&B/football/actor namesake matches)
                    bool isBasketballBio = wikitext.Contains("Infobox basketball", StringComparison.OrdinalIgnoreCase)
                        || (wikitext.Contains("basketball player", StringComparison.OrdinalIgnoreCase)
                            && !wikitext.Contains("Infobox football biography", StringComparison.OrdinalIgnoreCase)
                            && !wikitext.Contains("Infobox musical artist", StringComparison.OrdinalIgnoreCase));
                    if (!isBasketballBio)
                    {
                        continue;
                    }

                    var bio = ParseWikitextBio(wikitext);
                    var dto = TryApplyBioUpdate(
                        player,
                        bio.BirthDate,
                        bio.HeightCm,
                        bio.WeightKg,
                        bio.WeightPounds,
                        bio.Position,
                        bio.Nationality,
                        bio.DraftYear,
                        bio.DraftPick,
                        bio.HeightStr);
                    if (dto != null)
                    {
                        enriched.Add(dto);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Error fetching Wikipedia batch");
        }

        return enriched;
    }

    private DateTime? ValidatePlausibleBirthDate(Player player, DateTime? candidate)
    {
        if (!candidate.HasValue) return null;
        int birthYear = candidate.Value.Year;
        if (birthYear < 1975 || birthYear > 2011) return null;

        int maxSeason = player.SeasonStats.Any() ? player.SeasonStats.Max(s => s.Season) : DateTime.UtcNow.Year;
        int impliedAge = maxSeason - birthYear;
        if (impliedAge < 15 || impliedAge > 44) return null;

        bool hasRecentCollege = string.Equals(player.League, "NCAAD1", StringComparison.OrdinalIgnoreCase)
            || player.SeasonStats.Any(s => string.Equals(s.League, "NCAAD1", StringComparison.OrdinalIgnoreCase) && s.Season >= 2022);
        int maxCollegeYear = player.SeasonStats.Any(s => string.Equals(s.League, "NCAAD1", StringComparison.OrdinalIgnoreCase))
            ? player.SeasonStats.Where(s => string.Equals(s.League, "NCAAD1", StringComparison.OrdinalIgnoreCase)).Max(s => s.Season)
            : maxSeason;
        if (hasRecentCollege && (maxCollegeYear - birthYear) > 26)
        {
            return null;
        }

        return DateTime.SpecifyKind(candidate.Value.Date, DateTimeKind.Utc);
    }

    private EnrichedPlayerEntryDto? TryApplyBioUpdate(
        Player player,
        DateTime? candidateBirthDate,
        int? heightCm,
        int? weightKg,
        int? weightPounds,
        string? position,
        string? nationality,
        int? draftYear,
        int? draftPick,
        string? heightStr = null)
    {
        DateTime? validatedDob = player.BirthDate == null
            ? ValidatePlausibleBirthDate(player, candidateBirthDate)
            : null;

        bool shouldUpdateNat = !string.IsNullOrWhiteSpace(nationality) &&
            (string.IsNullOrWhiteSpace(player.Nationality) || player.Nationality == "INTL");

        bool hasHeight = heightCm.HasValue && player.HeightCm == null && heightCm.Value >= 160 && heightCm.Value <= 240;
        bool hasWeight = weightKg.HasValue && player.WeightKg == null && weightKg.Value >= 55 && weightKg.Value <= 180;
        bool hasPos = !string.IsNullOrWhiteSpace(position) && player.PrimaryPosition == null;
        bool hasDraft = (draftYear.HasValue && player.DraftYear == null) || (draftPick.HasValue && player.DraftPick == null);

        if (!validatedDob.HasValue && !hasHeight && !hasWeight && !hasPos && !shouldUpdateNat && !hasDraft)
            return null;

        player.UpdateDetails(
            firstName: player.FirstName,
            lastName: player.LastName,
            position: hasPos ? position : player.Position,
            height: hasHeight ? (heightStr ?? player.Height) : player.Height,
            weightPounds: (weightPounds.HasValue && player.WeightPounds == null) ? weightPounds : player.WeightPounds,
            teamName: player.TeamName,
            league: player.League,
            nationality: shouldUpdateNat ? nationality : player.Nationality,
            birthDate: player.BirthDate ?? validatedDob,
            heightCm: hasHeight ? heightCm : player.HeightCm,
            weightKg: hasWeight ? weightKg : player.WeightKg,
            wingspanCm: player.WingspanCm,
            draftYear: player.DraftYear ?? draftYear,
            draftPick: player.DraftPick ?? draftPick);

        return new EnrichedPlayerEntryDto(
            FullName: player.FullName,
            League: player.League,
            BirthDate: player.BirthDate?.ToString("yyyy-MM-dd"),
            Age: player.CurrentAge,
            HeightCm: player.HeightCm,
            WeightKg: player.WeightKg,
            Position: player.PrimaryPosition ?? player.Position,
            DraftYear: player.DraftYear);
    }

    private sealed record EuroFeedBio(DateTime? BirthDate, int? HeightCm, int? WeightKg, string? Nationality);

    private async Task<(Dictionary<string, EuroFeedBio> ByName, Dictionary<string, EuroFeedBio> ByDobLast)> BuildEuroLeagueFeedBioIndexAsync(CancellationToken ct)
    {
        var index = new Dictionary<string, EuroFeedBio>(StringComparer.OrdinalIgnoreCase);
        var byDobLast = new Dictionary<string, EuroFeedBio>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<(string Comp, int Year)>
        {
            ("E", 2027), ("U", 2027),
            ("E", 2026), ("U", 2026),
            ("E", 2025), ("U", 2025),
            ("E", 2024), ("U", 2024),
            ("E", 2023), ("U", 2023),
            ("E", 2022), ("U", 2022)
        };

        foreach (var (comp, year) in targets)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                string url = $"https://feeds.incrowdsports.com/provider/euroleague-feeds/v2/competitions/{comp}/seasons/{comp}{year}/people?limit=1500&personType=J";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                var resp = await _httpClient.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var entry in dataArr.EnumerateArray())
                {
                    if (!entry.TryGetProperty("person", out var person)) continue;
                    string? rawName = person.TryGetProperty("name", out var np) ? np.GetString() : null;
                    if (string.IsNullOrWhiteSpace(rawName)) continue;

                    // Format is "LAST, FIRST"
                    string full = rawName.Contains(',')
                        ? string.Join(" ", rawName.Split(',', 2).Select(x => x.Trim()).Reverse())
                        : rawName;
                    string normKey = Player.NormalizeName(full, string.Empty);
                    if (string.IsNullOrWhiteSpace(normKey)) continue;

                    DateTime? dob = null;
                    if (person.TryGetProperty("birthDate", out var bd) && bd.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(bd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDob))
                    {
                        dob = DateTime.SpecifyKind(parsedDob.Date, DateTimeKind.Utc);
                    }

                    int? h = person.TryGetProperty("height", out var hp) && hp.ValueKind == JsonValueKind.Number && hp.TryGetInt32(out int hv) && hv >= 160 && hv <= 240 ? hv : null;
                    int? w = person.TryGetProperty("weight", out var wp) && wp.ValueKind == JsonValueKind.Number && wp.TryGetInt32(out int wv) && wv >= 55 && wv <= 180 ? wv : null;
                    string? nat = null;
                    if (person.TryGetProperty("country", out var cp))
                    {
                        string? code = cp.TryGetProperty("code", out var cCode) ? cCode.GetString() : null;
                        string? cname = cp.TryGetProperty("name", out var cName) ? cName.GetString() : null;
                        nat = NormalizeCountryOrCode(code, cname);
                    }

                    if (!dob.HasValue && !h.HasValue && !w.HasValue && nat == null) continue;

                    if (!index.TryGetValue(normKey, out var existing))
                    {
                        index[normKey] = new EuroFeedBio(dob, h, w, nat);
                    }
                    else
                    {
                        index[normKey] = new EuroFeedBio(
                            existing.BirthDate ?? dob,
                            existing.HeightCm ?? h,
                            existing.WeightKg ?? w,
                            existing.Nationality ?? nat);
                    }

                    if (dob.HasValue)
                    {
                        string rawLast = rawName.Contains(',')
                            ? rawName.Split(',', 2)[0].Trim()
                            : (full.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "");
                        string dobLastKey = $"{Player.NormalizeName(rawLast, string.Empty)}|{dob.Value:yyyy-MM-dd}";
                        if (!byDobLast.TryGetValue(dobLastKey, out var exDl))
                        {
                            byDobLast[dobLastKey] = new EuroFeedBio(dob, h, w, nat);
                        }
                        else
                        {
                            byDobLast[dobLastKey] = new EuroFeedBio(
                                exDl.BirthDate ?? dob,
                                exDl.HeightCm ?? h,
                                exDl.WeightKg ?? w,
                                exDl.Nationality ?? nat);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "EuroLeague v2 people fetch failed for {Comp}{Year}", comp, year);
            }
        }

        return (index, byDobLast);
    }

    private async Task<List<(Player Player, EnrichedPlayerEntryDto Dto)>> EnrichFromWikidataAsync(List<Player> players, CancellationToken ct)
    {
        var enriched = new List<(Player, EnrichedPlayerEntryDto)>();
        var playerCandidates = new ConcurrentDictionary<Guid, (Player Player, List<string> Qids)>();

        using var throttle = new SemaphoreSlim(2);
        int rateLimited = 0;

        var searchTasks = players.Select(async p =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested || Volatile.Read(ref rateLimited) > 0) return;

                var queries = BuildSearchQueries(p.FullName);
                var qids = new List<string>();
                foreach (var q in queries)
                {
                    if (Volatile.Read(ref rateLimited) > 0) break;
                    try
                    {
                        await Task.Delay(90, ct);
                        string searchUrl = $"https://www.wikidata.org/w/api.php?action=wbsearchentities&search={Uri.EscapeDataString(q)}&language=en&format=json&type=item&limit=4";
                        var resp = await _httpClient.GetAsync(searchUrl, ct);
                        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            Interlocked.Exchange(ref rateLimited, 1);
                            await Task.Delay(1500, ct);
                            break;
                        }
                        if (!resp.IsSuccessStatusCode) continue;

                        var json = await resp.Content.ReadAsStringAsync(ct);
                        using var doc = JsonDocument.Parse(json);
                        if (!doc.RootElement.TryGetProperty("search", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;

                        foreach (var item in arr.EnumerateArray())
                        {
                            string? id = item.TryGetProperty("id", out var idp) ? idp.GetString() : null;
                            string desc = item.TryGetProperty("description", out var dp) ? (dp.GetString() ?? "") : "";
                            string label = item.TryGetProperty("label", out var lp) ? (lp.GetString() ?? "") : "";
                            if (string.IsNullOrWhiteSpace(id)) continue;
                            if (desc.Contains("painter", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("politician", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("musician", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("footballer", StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!NameReasonablyMatches(p, label) && !desc.Contains("basketball", StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!qids.Contains(id)) qids.Add(id);
                        }
                        if (qids.Count > 0) break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogDebug(ex, "Wikidata search error for {Name}", p.FullName);
                    }
                }

                if (qids.Count > 0)
                {
                    playerCandidates[p.Id] = (p, qids.Take(3).ToList());
                }
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        await Task.WhenAll(searchTasks);

        if (playerCandidates.IsEmpty) return enriched;

        var allQids = playerCandidates.Values.SelectMany(v => v.Qids).Distinct().ToList();
        var entityMap = new Dictionary<string, (DateTime? Dob, int? HeightCm, int? WeightKg, List<string> CountryQids, bool IsBasketball, Dictionary<string, string> Sitelinks)>();

        for (int i = 0; i < allQids.Count; i += 40)
        {
            if (ct.IsCancellationRequested) break;
            var batchIds = string.Join("|", allQids.Skip(i).Take(40));
            try
            {
                await Task.Delay(100, ct);
                string getUrl = $"https://www.wikidata.org/w/api.php?action=wbgetentities&ids={batchIds}&props=claims|descriptions|sitelinks&format=json";
                var resp = await _httpClient.GetAsync(getUrl, ct);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("entities", out var entities)) continue;

                foreach (var prop in entities.EnumerateObject())
                {
                    var ent = prop.Value;
                    bool isBball = false;
                    if (ent.TryGetProperty("descriptions", out var descs) && descs.TryGetProperty("en", out var enDesc))
                    {
                        string? dv = enDesc.TryGetProperty("value", out var dvp) ? dvp.GetString() : null;
                        if (dv?.Contains("basketball", StringComparison.OrdinalIgnoreCase) == true) isBball = true;
                    }

                    DateTime? dob = null;
                    int? hCm = null;
                    int? wKg = null;
                    var countryQids = new List<string>();

                    if (ent.TryGetProperty("claims", out var claims))
                    {
                        // P106 occupation -> Q3665646 basketball player
                        if (claims.TryGetProperty("P106", out var occArr))
                        {
                            foreach (var claim in occArr.EnumerateArray())
                            {
                                string? occId = TryGetWikidataEntityId(claim);
                                if (occId == "Q3665646") isBball = true;
                            }
                        }
                        // P641 sport -> Q5372 basketball
                        if (claims.TryGetProperty("P641", out var sportArr))
                        {
                            foreach (var claim in sportArr.EnumerateArray())
                            {
                                if (TryGetWikidataEntityId(claim) == "Q5372") isBball = true;
                            }
                        }

                        if (claims.TryGetProperty("P569", out var dobArr))
                        {
                            foreach (var claim in dobArr.EnumerateArray())
                            {
                                dob = TryGetWikidataTime(claim);
                                if (dob.HasValue) break;
                            }
                        }
                        if (claims.TryGetProperty("P2048", out var hArr))
                        {
                            foreach (var claim in hArr.EnumerateArray())
                            {
                                hCm = TryGetWikidataHeightCm(claim);
                                if (hCm.HasValue) break;
                            }
                        }
                        if (claims.TryGetProperty("P2067", out var wArr))
                        {
                            foreach (var claim in wArr.EnumerateArray())
                            {
                                wKg = TryGetWikidataWeightKg(claim);
                                if (wKg.HasValue) break;
                            }
                        }
                        // P27 country of citizenship
                        if (claims.TryGetProperty("P27", out var natArr))
                        {
                            foreach (var claim in natArr.EnumerateArray())
                            {
                                string? cid = TryGetWikidataEntityId(claim);
                                if (!string.IsNullOrWhiteSpace(cid) && !countryQids.Contains(cid))
                                    countryQids.Add(cid);
                            }
                        }
                    }

                    var sitelinks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (ent.TryGetProperty("sitelinks", out var slObj))
                    {
                        foreach (var sl in slObj.EnumerateObject())
                        {
                            if (sl.Value.TryGetProperty("title", out var tp) && tp.GetString() is string t)
                                sitelinks[sl.Name] = t;
                        }
                    }

                    entityMap[prop.Name] = (dob, hCm, wKg, countryQids, isBball, sitelinks);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Wikidata wbgetentities failed");
            }
        }

        // Resolve any unknown P27 country QIDs via one batched labels query
        var resolvedCountryIso = new Dictionary<string, string>(WikidataCountryQidToIso, StringComparer.OrdinalIgnoreCase);
        var unknownCountryQids = entityMap.Values
            .Where(v => v.IsBasketball)
            .SelectMany(v => v.CountryQids)
            .Where(cid => !resolvedCountryIso.ContainsKey(cid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < unknownCountryQids.Count; i += 40)
        {
            if (ct.IsCancellationRequested) break;
            var batchCids = string.Join("|", unknownCountryQids.Skip(i).Take(40));
            try
            {
                await Task.Delay(80, ct);
                string labelUrl = $"https://www.wikidata.org/w/api.php?action=wbgetentities&ids={batchCids}&props=labels&languages=en&format=json";
                var resp = await _httpClient.GetAsync(labelUrl, ct);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("entities", out var entities)) continue;

                foreach (var prop in entities.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("labels", out var labels) &&
                        labels.TryGetProperty("en", out var enLabel) &&
                        enLabel.TryGetProperty("value", out var vp))
                    {
                        string? iso = NormalizeCountryOrCode(null, vp.GetString());
                        if (!string.IsNullOrWhiteSpace(iso))
                            resolvedCountryIso[prop.Name] = iso;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Wikidata country QID label lookup failed");
            }
        }

        foreach (var (player, qids) in playerCandidates.Values)
        {
            foreach (var qid in qids)
            {
                if (!entityMap.TryGetValue(qid, out var info) || !info.IsBasketball) continue;

                string? resolvedNat = ResolveWikidataNationality(info.CountryQids, resolvedCountryIso);
                var dto = TryApplyBioUpdate(player, info.Dob, info.HeightCm, info.WeightKg, null, null, resolvedNat, null, null);
                if (dto != null)
                {
                    enriched.Add((player, dto));
                    break;
                }

                // If Wikidata confirmed basketball player QID but omitted DOB/Nationality, try its Wikipedia sitelink
                if (NeedsDobOrNationality(player) && info.Sitelinks.Count > 0)
                {
                    var langPriority = GetWikiLanguagesForLeague(player.League);
                    foreach (var lang in langPriority)
                    {
                        if (info.Sitelinks.TryGetValue($"{lang}wiki", out var localTitle))
                        {
                            var localBio = await FetchLocalizedWikipediaPageAsync(lang, localTitle, ct);
                            if (localBio != null)
                            {
                                var localDto = TryApplyBioUpdate(player, localBio.BirthDate, localBio.HeightCm, localBio.WeightKg, localBio.WeightPounds, localBio.Position, localBio.Nationality, localBio.DraftYear, localBio.DraftPick, localBio.HeightStr);
                                if (localDto != null)
                                {
                                    enriched.Add((player, localDto));
                                    break;
                                }
                            }
                        }
                    }
                    if (!NeedsDobOrNationality(player)) break;
                }
            }
        }

        return enriched;
    }

    private async Task<List<(Player Player, EnrichedPlayerEntryDto Dto)>> EnrichFromBasketballReferenceAsync(List<Player> players, CancellationToken ct)
    {
        var enriched = new List<(Player, EnrichedPlayerEntryDto)>();
        var targetLeagues = players.Select(p => p.League).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Build NormalizedName -> BRef relative player page URL map from international + gleague + NBA tables
        var leagueIndexUrls = new List<string>();
        foreach (var def in GlobalLeagueEndpointCatalog.GetTableScrapedLeagues())
        {
            if (def.PrimaryUrlTemplate.Contains("basketball-reference.com", StringComparison.OrdinalIgnoreCase))
            {
                bool needed = targetLeagues.Contains(def.LeagueCode, StringComparer.OrdinalIgnoreCase)
                    || (def.LeagueCode is "ACB" or "LNB" or "BSL" or "LegaA" or "ABA" or "VTB" or "GBL" or "EuroCup" or "EuroLeague" && targetLeagues.Contains("BCL", StringComparer.OrdinalIgnoreCase));
                if (!needed) continue;

                leagueIndexUrls.Add(def.BuildPrimaryUrl(2027));
                leagueIndexUrls.Add(def.BuildPrimaryUrl(2026));
            }
        }

        var playerUrlMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableUrl in leagueIndexUrls.Distinct())
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await Task.Delay(120, ct);
                using var req = new HttpRequestMessage(HttpMethod.Get, tableUrl);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                var resp = await _httpClient.SendAsync(req, ct);
                if (resp.StatusCode == HttpStatusCode.TooManyRequests) break;
                if (!resp.IsSuccessStatusCode) continue;

                var html = await resp.Content.ReadAsStringAsync(ct);
                foreach (Match m in BRefLinkRegex().Matches(html))
                {
                    string relUrl = m.Groups[1].Value;
                    string rawText = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                    string norm = Player.NormalizeName(rawText, string.Empty);
                    if (!string.IsNullOrWhiteSpace(norm) && !playerUrlMap.ContainsKey(norm))
                    {
                        playerUrlMap[norm] = relUrl;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "BRef league index fetch skipped for {Url}", tableUrl);
            }
        }

        if (playerUrlMap.Count == 0) return enriched;

        foreach (var p in players)
        {
            if (ct.IsCancellationRequested) break;
            var key = NormalizeLookupKey(p);
            if (!playerUrlMap.TryGetValue(key, out var relUrl)) continue;

            try
            {
                await Task.Delay(200, ct);
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.basketball-reference.com{relUrl}");
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                var resp = await _httpClient.SendAsync(req, ct);
                if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Basketball-Reference HTTP 429 received during player bio lookup; halting BRef pass cleanly.");
                    break;
                }
                if (!resp.IsSuccessStatusCode) continue;

                var html = await resp.Content.ReadAsStringAsync(ct);
                var parsed = ParseBasketballReferencePlayerPage(html);
                var dto = TryApplyBioUpdate(p, parsed.BirthDate, parsed.HeightCm, parsed.WeightKg, parsed.WeightPounds, null, parsed.Nationality, null, null, parsed.HeightStr);
                if (dto != null)
                {
                    enriched.Add((p, dto));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "BRef bio fetch failed for {Name}", p.FullName);
            }
        }

        return enriched;
    }

    private async Task<List<(Player Player, EnrichedPlayerEntryDto Dto)>> EnrichFromMultiLanguageWikipediaAsync(List<Player> players, CancellationToken ct)
    {
        var enriched = new List<(Player, EnrichedPlayerEntryDto)>();

        foreach (var p in players)
        {
            if (ct.IsCancellationRequested) break;
            foreach (var lang in GetWikiLanguagesForLeague(p.League).Where(l => l != "en").Take(2))
            {
                try
                {
                    await Task.Delay(80, ct);
                    string query = $"{p.FullName}";
                    string searchUrl = $"https://{lang}.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&srlimit=2&format=json";
                    var resp = await _httpClient.GetAsync(searchUrl, ct);
                    if (!resp.IsSuccessStatusCode) continue;

                    var json = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("query", out var qElem) || !qElem.TryGetProperty("search", out var sArr))
                        continue;

                    foreach (var hit in sArr.EnumerateArray())
                    {
                        string title = hit.GetProperty("title").GetString() ?? "";
                        string snippet = hit.TryGetProperty("snippet", out var sp) ? (sp.GetString() ?? "") : "";
                        if (!NameReasonablyMatches(p, title)) continue;
                        if (!snippet.Contains("basket", StringComparison.OrdinalIgnoreCase) &&
                            !snippet.Contains("baloncest", StringComparison.OrdinalIgnoreCase) &&
                            !snippet.Contains("baschet", StringComparison.OrdinalIgnoreCase) &&
                            !snippet.Contains("košark", StringComparison.OrdinalIgnoreCase) &&
                            !snippet.Contains("καλαθοσφ", StringComparison.OrdinalIgnoreCase) &&
                            !snippet.Contains("баскетбол", StringComparison.OrdinalIgnoreCase) &&
                            !snippet.Contains("krepšin", StringComparison.OrdinalIgnoreCase) &&
                            !snippet.Contains("pallacanestr", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var localBio = await FetchLocalizedWikipediaPageAsync(lang, title, ct);
                        if (localBio != null)
                        {
                            var dto = TryApplyBioUpdate(p, localBio.BirthDate, localBio.HeightCm, localBio.WeightKg, localBio.WeightPounds, localBio.Position, localBio.Nationality, localBio.DraftYear, localBio.DraftPick, localBio.HeightStr);
                            if (dto != null)
                            {
                                enriched.Add((p, dto));
                                break;
                            }
                        }
                    }
                    if (!NeedsDobOrNationality(p)) break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Multi-lang wiki fallback error for {Name} on {Lang}", p.FullName, lang);
                }
            }
        }

        return enriched;
    }

    private async Task<ParsedPlayerBio?> FetchLocalizedWikipediaPageAsync(string lang, string title, CancellationToken ct)
    {
        try
        {
            await Task.Delay(70, ct);
            string url = $"https://{lang}.wikipedia.org/w/api.php?action=query&redirects=1&prop=revisions&rvprop=content&rvsection=0&titles={Uri.EscapeDataString(title)}&format=json";
            var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("query", out var qElem) || !qElem.TryGetProperty("pages", out var pages)) return null;

            foreach (var prop in pages.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("missing", out _)) continue;
                if (!prop.Value.TryGetProperty("revisions", out var revs) || revs.GetArrayLength() == 0) continue;
                string wikitext = revs[0].GetProperty("*").GetString() ?? "";
                if (wikitext.Contains("football", StringComparison.OrdinalIgnoreCase) && !wikitext.Contains("basketball", StringComparison.OrdinalIgnoreCase))
                    continue;
                return ParseLocalizedWikitextBio(wikitext);
            }
        }
        catch { }
        return null;
    }

    private static ParsedPlayerBio ParseLocalizedWikitextBio(string wikitext)
    {
        var baseBio = ParseWikitextBio(wikitext);
        if (baseBio.BirthDate.HasValue) return baseBio;

        // Handle localized templates like {{Fecha de nacimiento|DD|MM|YYYY}}, {{Date de naissance|DD|MM|YYYY}}, {{Doğum tarihi ve yaşı|YYYY|MM|DD}}, {{Geburtsdatum|...}}
        DateTime? localDob = null;
        var ymd = Regex.Match(wikitext, @"(?:birth_date|fecha(?: de)? nacimiento|date de naissance|data di nascita|do[gğ]um tarihi|datum ro[dđ]enja|gimimo data)[^=\n]*=\s*(?:\{\{[^|{}]+\|)?(?:(?:df|mf)=\w+\|)?(\d{4})\|(\d{1,2})\|(\d{1,2})", RegexOptions.IgnoreCase);
        if (ymd.Success && int.TryParse(ymd.Groups[1].Value, out int y) && int.TryParse(ymd.Groups[2].Value, out int m) && int.TryParse(ymd.Groups[3].Value, out int d))
        {
            if (y >= 1975 && y <= 2011 && m >= 1 && m <= 12 && d >= 1 && d <= 31)
            {
                try { localDob = new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc); } catch { }
            }
        }

        if (!localDob.HasValue)
        {
            var dmy = Regex.Match(wikitext, @"\{\{(?:Fecha de nacimiento|Date de naissance|Datum ro[dđ]enja|Geburtsdatum)[^|{}]*\|(\d{1,2})\|(\d{1,2})\|(\d{4})", RegexOptions.IgnoreCase);
            if (dmy.Success && int.TryParse(dmy.Groups[1].Value, out int dd) && int.TryParse(dmy.Groups[2].Value, out int mm) && int.TryParse(dmy.Groups[3].Value, out int yy))
            {
                if (yy >= 1975 && yy <= 2011 && mm >= 1 && mm <= 12 && dd >= 1 && dd <= 31)
                {
                    try { localDob = new DateTime(yy, mm, dd, 0, 0, 0, DateTimeKind.Utc); } catch { }
                }
            }
        }

        return baseBio with { BirthDate = localDob };
    }

    internal static ParsedPlayerBio ParseBasketballReferencePlayerPage(string html)
    {
        DateTime? dob = null;
        var mDob = Regex.Match(html, @"data-birth=""(\d{4}-\d{2}-\d{2})""");
        if (mDob.Success && DateTime.TryParseExact(mDob.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDob))
        {
            dob = DateTime.SpecifyKind(parsedDob.Date, DateTimeKind.Utc);
        }

        int? hCm = null;
        string? hStr = null;
        var mHeight = Regex.Match(html, @"\((\d{3})cm(?:,\s*(\d{2,3})kg)?\)", RegexOptions.IgnoreCase);
        if (mHeight.Success && int.TryParse(mHeight.Groups[1].Value, out int cm) && cm >= 160 && cm <= 240)
        {
            hCm = cm;
            int totalInches = (int)Math.Round(cm / 2.54);
            hStr = $"{totalInches / 12}-{totalInches % 12}";
        }

        int? wKg = null;
        int? wLbs = null;
        if (mHeight.Success && mHeight.Groups[2].Success && int.TryParse(mHeight.Groups[2].Value, out int kg) && kg >= 55 && kg <= 180)
        {
            wKg = kg;
            wLbs = (int)Math.Round(kg / 0.45359237);
        }

        string? nat = null;
        var mNat = Regex.Match(html, @"Nationality:\s*</strong>\s*(?:<[^>]+>)*([^<\r\n]+)", RegexOptions.IgnoreCase);
        if (mNat.Success)
        {
            nat = NormalizeCountryOrCode(null, WebUtility.HtmlDecode(mNat.Groups[1].Value).Trim());
        }
        if (nat == null)
        {
            var mIso2 = Regex.Match(html, @"/friv/birthplaces\.fcgi\?country=([A-Z]{2})(?:&|""|')", RegexOptions.IgnoreCase);
            if (mIso2.Success && Iso2ToIso3Map.TryGetValue(mIso2.Groups[1].Value, out var iso3))
            {
                nat = iso3;
            }
        }

        return new ParsedPlayerBio(dob, hCm, hStr, wKg, wLbs, null, null, null, nat);
    }

    private static string[] BuildSearchQueries(string fullName)
    {
        var list = new List<string>();
        var trimmed = fullName.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed)) list.Add(trimmed);
        var noDots = trimmed.Replace(".", "");
        if (!string.Equals(noDots, trimmed, StringComparison.OrdinalIgnoreCase)) list.Add(noDots);
        return list.ToArray();
    }

    private static bool NameReasonablyMatches(Player player, string candidateLabel)
    {
        var pNorm = Player.NormalizeName(player.FirstName, player.LastName);
        var cNorm = Player.NormalizeName(candidateLabel, string.Empty);
        if (string.IsNullOrWhiteSpace(pNorm) || string.IsNullOrWhiteSpace(cNorm)) return false;
        if (string.Equals(pNorm, cNorm, StringComparison.OrdinalIgnoreCase)) return true;
        var lastNorm = Player.NormalizeName(player.LastName, string.Empty);
        return lastNorm.Length >= 3 && cNorm.Contains(lastNorm, StringComparison.OrdinalIgnoreCase) &&
               (pNorm[0] == cNorm[0]);
    }

    private static IReadOnlyList<string> GetWikiLanguagesForLeague(string? league) => league?.ToUpperInvariant() switch
    {
        "ACB" => new[] { "es", "en" },
        "LNB" => new[] { "fr", "en" },
        "BBL" => new[] { "de", "en" },
        "LEGAA" => new[] { "it", "en" },
        "BSL" => new[] { "tr", "en" },
        "GBL" => new[] { "el", "en" },
        "LKL" => new[] { "lt", "en" },
        "ABA" => new[] { "sr", "hr", "sl", "en" },
        "VTB" => new[] { "ru", "en" },
        "BCL" or "EUROCUP" or "EUROLEAGUE" => new[] { "es", "fr", "de", "it", "tr", "sr", "el", "lt", "en" },
        _ => new[] { "en", "es", "fr" }
    };

    private static string? TryGetWikidataEntityId(JsonElement claim)
    {
        if (claim.TryGetProperty("mainsnak", out var ms) &&
            ms.TryGetProperty("datavalue", out var dv) &&
            dv.TryGetProperty("value", out var val) &&
            val.TryGetProperty("id", out var idp))
        {
            return idp.GetString();
        }
        return null;
    }

    private static DateTime? TryGetWikidataTime(JsonElement claim)
    {
        if (claim.TryGetProperty("mainsnak", out var ms) &&
            ms.TryGetProperty("datavalue", out var dv) &&
            dv.TryGetProperty("value", out var val) &&
            val.TryGetProperty("time", out var tp))
        {
            var raw = tp.GetString(); // e.g. +2001-09-12T00:00:00Z
            if (!string.IsNullOrWhiteSpace(raw) && raw.Length >= 11)
            {
                var datePart = raw.TrimStart('+').Substring(0, 10);
                if (!datePart.EndsWith("-00", StringComparison.Ordinal) &&
                    DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                {
                    return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
                }
            }
        }
        return null;
    }

    private static int? TryGetWikidataHeightCm(JsonElement claim)
    {
        if (claim.TryGetProperty("mainsnak", out var ms) &&
            ms.TryGetProperty("datavalue", out var dv) &&
            dv.TryGetProperty("value", out var val) &&
            val.TryGetProperty("amount", out var ap) &&
            decimal.TryParse(ap.GetString()?.TrimStart('+'), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rawVal))
        {
            int cm = rawVal < 3.0m ? (int)Math.Round(rawVal * 100m) : (int)Math.Round(rawVal);
            if (cm >= 160 && cm <= 240) return cm;
        }
        return null;
    }

    private static int? TryGetWikidataWeightKg(JsonElement claim)
    {
        if (claim.TryGetProperty("mainsnak", out var ms) &&
            ms.TryGetProperty("datavalue", out var dv) &&
            dv.TryGetProperty("value", out var val) &&
            val.TryGetProperty("amount", out var ap) &&
            decimal.TryParse(ap.GetString()?.TrimStart('+'), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rawVal))
        {
            int kg = (int)Math.Round(rawVal);
            if (kg >= 55 && kg <= 180) return kg;
        }
        return null;
    }

    private static string? NormalizeCountryOrCode(string? code, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && CountryToIsoMap.TryGetValue(name.Trim(), out var mapped))
            return mapped;
        if (!string.IsNullOrWhiteSpace(code) && Regex.IsMatch(code.Trim(), @"^[A-Z]{3}$"))
            return code.Trim().ToUpperInvariant();
        return null;
    }

    [GeneratedRegex(@"href=""(/(?:international/players|gleague/players|players)/[^""]+\.html)""[^>]*>([^<]+)<", RegexOptions.IgnoreCase)]
    private static partial Regex BRefLinkRegex();

    private static string CleanWikiTitle(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        // Preserve generational suffixes (Jr., Sr., III) so players like Tim Hardaway Jr. and Jaren Jackson Jr. don't resolve to their fathers
        return fullName.Trim().Replace(" ", "_");
    }

    internal static ParsedPlayerBio ParseWikitextBio(string wikitext)
    {
        DateTime? birthDate = ExtractBirthDate(wikitext);
        var (heightCm, heightStr) = ExtractHeight(wikitext);
        var (weightKg, weightPounds) = ExtractWeight(wikitext);
        string? position = ExtractPosition(wikitext);
        var (draftYear, draftPick) = ExtractDraft(wikitext);
        string? nationality = ExtractNationality(wikitext);

        return new ParsedPlayerBio(birthDate, heightCm, heightStr, weightKg, weightPounds, position, draftYear, draftPick, nationality);
    }

    private static DateTime? ExtractBirthDate(string text)
    {
        // 1. {{birth date and age|YYYY|MM|DD}} or {{birth date and age|df=yes|YYYY|MM|DD}}
        var m1 = Regex.Match(text, @"\{\{[Bb]irth date[^\|}]*\|(?:df=\w+\|)?(?:mf=\w+\|)?(\d{4})\|(\d{1,2})\|(\d{1,2})", RegexOptions.IgnoreCase);
        if (m1.Success &&
            int.TryParse(m1.Groups[1].Value, out int y) &&
            int.TryParse(m1.Groups[2].Value, out int m) &&
            int.TryParse(m1.Groups[3].Value, out int d))
        {
            if (y >= 1975 && y <= 2010 && m >= 1 && m <= 12 && d >= 1 && d <= 31)
            {
                try { return new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc); } catch { }
            }
        }

        // 2. {{birth-date|DD Month YYYY}} or {{birth date|Month DD, YYYY}}
        var m2 = Regex.Match(text, @"\|\s*birth_date\s*=\s*(?:\{\{[^}]*\}\}\s*)?([A-Za-z0-9,\s]+)", RegexOptions.IgnoreCase);
        if (m2.Success)
        {
            string raw = m2.Groups[1].Value.Trim();
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            {
                if (dt.Year >= 1975 && dt.Year <= 2010)
                {
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
            }
        }

        // 3. (born Month DD, YYYY) or (born DD Month YYYY)
        var m3 = Regex.Match(text, @"\(born\s+([A-Za-z0-9,\s]+?)\)", RegexOptions.IgnoreCase);
        if (m3.Success)
        {
            string raw = m3.Groups[1].Value.Trim();
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            {
                if (dt.Year >= 1975 && dt.Year <= 2010)
                {
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
            }
        }

        return null;
    }

    private static (int? Cm, string? Str) ExtractHeight(string text)
    {
        // 1. height_m = 1.97 or 2.22
        var mMetric = Regex.Match(text, @"\|\s*height_m\s*=\s*([0-2]\.\d{1,2})", RegexOptions.IgnoreCase);
        if (mMetric.Success && decimal.TryParse(mMetric.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal meters))
        {
            int cm = (int)Math.Round(meters * 100);
            if (cm >= 160 && cm <= 240)
            {
                int totalInches = (int)Math.Round(cm / 2.54);
                string ftIn = $"{totalInches / 12}-{totalInches % 12}";
                return (cm, ftIn);
            }
        }

        // 2. height_cm = 197
        var mCm = Regex.Match(text, @"\|\s*height_cm\s*=\s*(\d{2,3})", RegexOptions.IgnoreCase);
        if (mCm.Success && int.TryParse(mCm.Groups[1].Value, out int cmVal) && cmVal >= 160 && cmVal <= 240)
        {
            int totalInches = (int)Math.Round(cmVal / 2.54);
            string ftIn = $"{totalInches / 12}-{totalInches % 12}";
            return (cmVal, ftIn);
        }

        // 3. height_ft = 6 | height_in = 8
        var mFt = Regex.Match(text, @"\|\s*height_ft\s*=\s*(\d)", RegexOptions.IgnoreCase);
        var mIn = Regex.Match(text, @"\|\s*height_in\s*=\s*(\d{1,2})", RegexOptions.IgnoreCase);
        if (mFt.Success && int.TryParse(mFt.Groups[1].Value, out int ft))
        {
            int inches = (mIn.Success && int.TryParse(mIn.Groups[1].Value, out int inh)) ? inh : 0;
            int cm = (int)Math.Round((ft * 12 + inches) * 2.54);
            if (cm >= 160 && cm <= 240)
            {
                return (cm, $"{ft}-{inches}");
            }
        }

        // 4. Fallback string like "6-8" or "6'8"
        var mStr = Regex.Match(text, @"\|\s*height\s*=\s*(\d)[\-'](\d{1,2})", RegexOptions.IgnoreCase);
        if (mStr.Success && int.TryParse(mStr.Groups[1].Value, out int f) && int.TryParse(mStr.Groups[2].Value, out int inch))
        {
            int cm = (int)Math.Round((f * 12 + inch) * 2.54);
            if (cm >= 160 && cm <= 240)
            {
                return (cm, $"{f}-{inch}");
            }
        }

        return (null, null);
    }

    private static (int? Kg, int? Lbs) ExtractWeight(string text)
    {
        // 1. weight_kg = 95
        var mKg = Regex.Match(text, @"\|\s*weight_kg\s*=\s*(\d{2,3})", RegexOptions.IgnoreCase);
        if (mKg.Success && int.TryParse(mKg.Groups[1].Value, out int kg) && kg >= 60 && kg <= 180)
        {
            int lbs = (int)Math.Round(kg / 0.45359237);
            return (kg, lbs);
        }

        // 2. weight_lb = 220
        var mLb = Regex.Match(text, @"\|\s*weight_lb\s*=\s*(\d{2,3})", RegexOptions.IgnoreCase);
        if (mLb.Success && int.TryParse(mLb.Groups[1].Value, out int lb) && lb >= 130 && lb <= 380)
        {
            int kgVal = (int)Math.Round(lb * 0.45359237);
            return (kgVal, lb);
        }

        return (null, null);
    }

    private static string? ExtractPosition(string text)
    {
        var m = Regex.Match(text, @"\|\s*position\s*=\s*(.+?)(?:\r|\n|$)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        string raw = m.Groups[1].Value;
        raw = Regex.Replace(raw, @"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", "$1"); // Strip [[Link|Text]] to Text
        raw = raw.Replace("{{", "").Replace("}}", "").Trim();

        if (raw.Contains("Point guard", StringComparison.OrdinalIgnoreCase) && raw.Contains("Shooting guard", StringComparison.OrdinalIgnoreCase))
            return "PG/SG";
        if (raw.Contains("Shooting guard", StringComparison.OrdinalIgnoreCase) && raw.Contains("Small forward", StringComparison.OrdinalIgnoreCase))
            return "SG/SF";
        if (raw.Contains("Small forward", StringComparison.OrdinalIgnoreCase) && raw.Contains("Power forward", StringComparison.OrdinalIgnoreCase))
            return "SF/PF";
        if (raw.Contains("Power forward", StringComparison.OrdinalIgnoreCase) && raw.Contains("Center", StringComparison.OrdinalIgnoreCase))
            return "PF/C";
        if (raw.Contains("Point guard", StringComparison.OrdinalIgnoreCase)) return "PG";
        if (raw.Contains("Shooting guard", StringComparison.OrdinalIgnoreCase)) return "SG";
        if (raw.Contains("Small forward", StringComparison.OrdinalIgnoreCase)) return "SF";
        if (raw.Contains("Power forward", StringComparison.OrdinalIgnoreCase)) return "PF";
        if (raw.Contains("Center", StringComparison.OrdinalIgnoreCase)) return "C";
        if (raw.Contains("Guard", StringComparison.OrdinalIgnoreCase)) return "G";
        if (raw.Contains("Forward", StringComparison.OrdinalIgnoreCase)) return "F";

        return null;
    }

    private static (int? Year, int? Pick) ExtractDraft(string text)
    {
        int? draftYear = null;
        int? draftPick = null;

        var mYear = Regex.Match(text, @"\|\s*draft_year\s*=\s*(\d{4})", RegexOptions.IgnoreCase);
        if (mYear.Success && int.TryParse(mYear.Groups[1].Value, out int y) && y >= 1970 && y <= 2030)
        {
            draftYear = y;
        }

        var mPick = Regex.Match(text, @"\|\s*draft_pick\s*=\s*(\d{1,2})", RegexOptions.IgnoreCase);
        if (mPick.Success && int.TryParse(mPick.Groups[1].Value, out int p) && p >= 1 && p <= 60)
        {
            draftPick = p;
        }

        return (draftYear, draftPick);
    }

    private static readonly Dictionary<string, string> CountryToIsoMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["united states"] = "USA", ["american"] = "USA", ["usa"] = "USA", ["u.s."] = "USA", ["us"] = "USA",
        ["turkey"] = "TUR", ["turkish"] = "TUR", ["türkiye"] = "TUR", ["turkiye"] = "TUR",
        ["serbia"] = "SRB", ["serbian"] = "SRB",
        ["croatia"] = "CRO", ["croatian"] = "CRO",
        ["greece"] = "GRE", ["greek"] = "GRE",
        ["spain"] = "ESP", ["spanish"] = "ESP",
        ["france"] = "FRA", ["french"] = "FRA",
        ["italy"] = "ITA", ["italian"] = "ITA",
        ["germany"] = "GER", ["german"] = "GER",
        ["lithuania"] = "LTU", ["lithuanian"] = "LTU",
        ["latvia"] = "LAT", ["latvian"] = "LAT",
        ["slovenia"] = "SLO", ["slovenian"] = "SLO",
        ["russia"] = "RUS", ["russian"] = "RUS",
        ["ukraine"] = "UKR", ["ukrainian"] = "UKR",
        ["montenegro"] = "MNE", ["montenegrin"] = "MNE",
        ["bosnia and herzegovina"] = "BIH", ["bosnian"] = "BIH",
        ["north macedonia"] = "MKD", ["macedonia"] = "MKD", ["macedonian"] = "MKD",
        ["canada"] = "CAN", ["canadian"] = "CAN",
        ["australia"] = "AUS", ["australian"] = "AUS",
        ["poland"] = "POL", ["polish"] = "POL",
        ["israel"] = "ISR", ["israeli"] = "ISR",
        ["argentina"] = "ARG", ["argentine"] = "ARG", ["argentinian"] = "ARG",
        ["brazil"] = "BRA", ["brazilian"] = "BRA",
        ["nigeria"] = "NGR", ["nigerian"] = "NGR",
        ["senegal"] = "SEN", ["senegalese"] = "SEN",
        ["cameroon"] = "CMR", ["cameroonian"] = "CMR",
        ["ivory coast"] = "CIV", ["côte d'ivoire"] = "CIV", ["ivorian"] = "CIV",
        ["united kingdom"] = "GBR", ["great britain"] = "GBR", ["british"] = "GBR", ["england"] = "GBR", ["english"] = "GBR",
        ["belgium"] = "BEL", ["belgian"] = "BEL",
        ["netherlands"] = "NED", ["dutch"] = "NED",
        ["switzerland"] = "SUI", ["swiss"] = "SUI",
        ["austria"] = "AUT", ["austrian"] = "AUT",
        ["czech republic"] = "CZE", ["czechia"] = "CZE", ["czech"] = "CZE",
        ["slovakia"] = "SVK", ["slovak"] = "SVK",
        ["hungary"] = "HUN", ["hungarian"] = "HUN",
        ["romania"] = "ROU", ["romanian"] = "ROU",
        ["bulgaria"] = "BUL", ["bulgarian"] = "BUL",
        ["estonia"] = "EST", ["estonian"] = "EST",
        ["finland"] = "FIN", ["finnish"] = "FIN",
        ["sweden"] = "SWE", ["swedish"] = "SWE",
        ["denmark"] = "DEN", ["danish"] = "DEN",
        ["georgia"] = "GEO", ["georgian"] = "GEO",
        ["puerto rico"] = "PUR", ["puerto rican"] = "PUR",
        ["dominican republic"] = "DOM", ["dominican"] = "DOM",
        ["south sudan"] = "SSD", ["south sudanese"] = "SSD",
        ["yugoslavia"] = "SRB", ["yugoslav"] = "SRB",
        ["united states of america"] = "USA",
        ["china"] = "CHN", ["people's republic of china"] = "CHN", ["chinese"] = "CHN",
        ["japan"] = "JPN", ["japanese"] = "JPN",
        ["south korea"] = "KOR", ["republic of korea"] = "KOR", ["korean"] = "KOR",
        ["new zealand"] = "NZL",
        ["bahamas"] = "BAH", ["the bahamas"] = "BAH", ["bahamian"] = "BAH",
        ["jamaica"] = "JAM", ["jamaican"] = "JAM",
        ["mali"] = "MLI", ["malian"] = "MLI",
        ["angola"] = "ANG", ["angolan"] = "ANG",
        ["egypt"] = "EGY", ["egyptian"] = "EGY",
        ["tunisia"] = "TUN", ["tunisian"] = "TUN",
        ["lebanon"] = "LBN", ["lebanese"] = "LBN",
        ["philippines"] = "PHI", ["filipino"] = "PHI",
        ["portugal"] = "POR", ["portuguese"] = "POR",
        ["norway"] = "NOR", ["norwegian"] = "NOR",
        ["iceland"] = "ISL", ["icelandic"] = "ISL",
        ["ireland"] = "IRL", ["irish"] = "IRL",
        ["democratic republic of the congo"] = "COD", ["dr congo"] = "COD",
        ["republic of the congo"] = "CGO",
        ["belarus"] = "BLR", ["belarusian"] = "BLR",
        ["kazakhstan"] = "KAZ", ["kazakhstani"] = "KAZ",
        ["iran"] = "IRI", ["iranian"] = "IRI",
        ["jordan"] = "JOR", ["jordanian"] = "JOR",
        ["gabon"] = "GAB", ["gabonese"] = "GAB",
        ["cape verde"] = "CPV", ["cabo verde"] = "CPV",
        ["south africa"] = "RSA", ["guinea"] = "GUI"
    };

    private static readonly Dictionary<string, string> Iso2ToIso3Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "USA", ["CA"] = "CAN", ["ES"] = "ESP", ["FR"] = "FRA", ["DE"] = "GER",
        ["IT"] = "ITA", ["RS"] = "SRB", ["HR"] = "CRO", ["GR"] = "GRE", ["TR"] = "TUR",
        ["LT"] = "LTU", ["SI"] = "SLO", ["LV"] = "LAT", ["RU"] = "RUS", ["AU"] = "AUS",
        ["GB"] = "GBR", ["UK"] = "GBR", ["BE"] = "BEL", ["PL"] = "POL", ["IL"] = "ISR",
        ["AR"] = "ARG", ["BR"] = "BRA", ["NG"] = "NGR", ["SN"] = "SEN", ["CM"] = "CMR",
        ["CI"] = "CIV", ["NL"] = "NED", ["CH"] = "SUI", ["AT"] = "AUT", ["CZ"] = "CZE",
        ["SK"] = "SVK", ["HU"] = "HUN", ["RO"] = "ROU", ["BG"] = "BUL", ["EE"] = "EST",
        ["FI"] = "FIN", ["SE"] = "SWE", ["DK"] = "DEN", ["GE"] = "GEO", ["UA"] = "UKR",
        ["ME"] = "MNE", ["BA"] = "BIH", ["MK"] = "MKD", ["PR"] = "PUR", ["DO"] = "DOM",
        ["SS"] = "SSD", ["BS"] = "BAH", ["JM"] = "JAM", ["NZ"] = "NZL", ["CN"] = "CHN",
        ["JP"] = "JPN", ["KR"] = "KOR", ["ML"] = "MLI", ["AO"] = "ANG", ["EG"] = "EGY",
        ["PT"] = "POR", ["NO"] = "NOR", ["IS"] = "ISL", ["IE"] = "IRL", ["CD"] = "COD"
    };

    private static readonly Dictionary<string, string> WikidataCountryQidToIso = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Q30"] = "USA", ["Q43"] = "TUR", ["Q29"] = "ESP", ["Q142"] = "FRA", ["Q183"] = "GER",
        ["Q38"] = "ITA", ["Q403"] = "SRB", ["Q41"] = "GRE", ["Q37"] = "LTU", ["Q224"] = "CRO",
        ["Q215"] = "SLO", ["Q159"] = "RUS", ["Q16"] = "CAN", ["Q408"] = "AUS", ["Q145"] = "GBR",
        ["Q31"] = "BEL", ["Q39"] = "SUI", ["Q211"] = "LAT", ["Q36"] = "POL", ["Q801"] = "ISR",
        ["Q414"] = "ARG", ["Q155"] = "BRA", ["Q1033"] = "NGR", ["Q1041"] = "SEN", ["Q1009"] = "CMR",
        ["Q1008"] = "CIV", ["Q55"] = "NED", ["Q40"] = "AUT", ["Q213"] = "CZE", ["Q214"] = "SVK",
        ["Q28"] = "HUN", ["Q218"] = "ROU", ["Q219"] = "BUL", ["Q191"] = "EST", ["Q33"] = "FIN",
        ["Q34"] = "SWE", ["Q35"] = "DEN", ["Q230"] = "GEO", ["Q212"] = "UKR", ["Q236"] = "MNE",
        ["Q225"] = "BIH", ["Q221"] = "MKD", ["Q1183"] = "PUR", ["Q786"] = "DOM", ["Q958"] = "SSD",
        ["Q778"] = "BAH", ["Q766"] = "JAM", ["Q664"] = "NZL", ["Q148"] = "CHN", ["Q17"] = "JPN",
        ["Q884"] = "KOR", ["Q912"] = "MLI", ["Q916"] = "ANG", ["Q79"] = "EGY", ["Q45"] = "POR"
    };

    private static readonly HashSet<string> DefunctWikidataCountryQids = new(StringComparer.OrdinalIgnoreCase)
    {
        "Q36704", // SFR Yugoslavia
        "Q15180", // Soviet Union
        "Q33946", // Czechoslovakia
        "Q83286"  // FR Yugoslavia
    };

    internal static string? ResolveWikidataNationality(IReadOnlyList<string> countryQids, IReadOnlyDictionary<string, string> isoMap)
    {
        if (countryQids.Count == 0) return null;

        var modernQids = countryQids.Where(q => !DefunctWikidataCountryQids.Contains(q)).ToList();
        var sourceQids = modernQids.Count > 0 ? modernQids : countryQids.ToList();

        var codes = new List<string>();
        foreach (var qid in sourceQids)
        {
            if (isoMap.TryGetValue(qid, out var iso) && !codes.Contains(iso))
            {
                codes.Add(iso);
            }
        }

        return codes.Count > 0 ? string.Join(" / ", codes.Take(2)) : null;
    }

    private static string? ExtractNationality(string text)
    {
        var codes = ParseNationalityTokens(text, @"\|\s*nationality\s*=\s*(.+?)(?:\r|\n)");
        if (codes.Count > 0) return string.Join(" / ", codes.Take(2));

        var ntCodes = ParseNationalityTokens(text, @"\|\s*(?:national_team|nat_team)\s*=\s*(.+?)(?:\r|\n)");
        if (ntCodes.Count > 0) return string.Join(" / ", ntCodes.Take(2));

        // Conservative fallback on unambiguous trailing sovereign country in birth_place (takes final segment only so US states like Georgia aren't misread as GEO)
        var mBirthPlace = Regex.Match(text, @"\|\s*birth_place\s*=\s*(.+?)(?:\r|\n)", RegexOptions.IgnoreCase);
        if (mBirthPlace.Success)
        {
            string cleaned = CleanWikiMarkup(mBirthPlace.Groups[1].Value);
            var segments = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 0)
            {
                var rawLast = segments[^1].Trim(' ', ':', ';', '*', '\t');
                var trimmedLast = rawLast.TrimEnd('.');
                if (CountryToIsoMap.TryGetValue(rawLast, out var bpIso) ||
                    CountryToIsoMap.TryGetValue(trimmedLast, out bpIso))
                {
                    return bpIso;
                }
            }
        }

        return null;
    }

    private static List<string> ParseNationalityTokens(string text, string fieldRegex)
    {
        var codes = new List<string>();
        var m = Regex.Match(text, fieldRegex, RegexOptions.IgnoreCase);
        if (!m.Success) return codes;

        string raw = CleanWikiMarkup(m.Groups[1].Value);
        foreach (var part in Regex.Split(raw, @"[/,]|\band\b|\|", RegexOptions.IgnoreCase))
        {
            var token = part.Trim(' ', '.', ':', ';', '[', ']', '*', '\t');
            if (CountryToIsoMap.TryGetValue(token, out var iso))
            {
                if (!codes.Contains(iso)) codes.Add(iso);
            }
            else if (Regex.IsMatch(token, @"^[A-Z]{3}$") && Iso2ToIso3Map.Values.Contains(token) && !codes.Contains(token))
            {
                codes.Add(token);
            }
        }
        return codes;
    }

    private static string CleanWikiMarkup(string raw)
    {
        raw = Regex.Replace(raw, @"<ref[^>]*>.*?</ref>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        raw = Regex.Replace(raw, @"<ref[^/>]*/?>", "", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, @"\{\{(?:flag(?:u|icon|country)?|bk(?:-rt)?)\|([^}|]+)(?:\|[^}]*)?\}\}", "$1", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, @"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", "$1");
        return raw.Replace("{{", "").Replace("}}", "");
    }

    internal record ParsedPlayerBio(
        DateTime? BirthDate,
        int? HeightCm,
        string? HeightStr,
        int? WeightKg,
        int? WeightPounds,
        string? Position,
        int? DraftYear,
        int? DraftPick,
        string? Nationality);
}
