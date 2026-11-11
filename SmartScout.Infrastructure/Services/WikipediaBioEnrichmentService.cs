using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Infrastructure.Services;

public class WikipediaBioEnrichmentService : IPlayerBioEnrichmentService
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
        int safeLimit = Math.Clamp(limit, 1, 500);

        var query = _dbContext.Players.AsQueryable();

        if (!string.IsNullOrWhiteSpace(league) && !league.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.League == league);
        }

        var targetPlayers = await query
            .Where(p => p.BirthDate == null || p.HeightCm == null || p.WeightKg == null || p.PrimaryPosition == null)
            .OrderByDescending(p => p.SeasonStats.Count)
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
        _logger.LogInformation("Starting Full Database Automated Player Bio Enrichment...");
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("  SMARTSCOUT DATABASE AUTOMATED BIO ENRICHMENT PIPELINE");
        Console.WriteLine("=======================================================\n");

        // 1. Fetch all players needing enrichment
        var allNeedingEnrichment = await _dbContext.Players
            .Where(p => p.BirthDate == null || p.HeightCm == null || p.WeightKg == null || p.PrimaryPosition == null)
            .OrderByDescending(p => p.SeasonStats.Count)
            .ToListAsync(cancellationToken);

        int totalFound = allNeedingEnrichment.Count;
        Console.WriteLine($"[1/3] Identified {totalFound:N0} players needing biological/physical enrichment in database.");

        if (totalFound == 0)
        {
            Console.WriteLine("All players in database already have complete physical and biographical metrics!");
            return new BioEnrichmentSummaryDto(0, 0, 0, new List<EnrichedPlayerEntryDto>());
        }

        // 2. Perform internal normalizations first (Height string -> HeightCm, Weight lbs -> WeightKg, Position -> Primary/Secondary)
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
            Console.WriteLine($"[2/3] Instant local normalization complete: {internalNormalizedCount:N0} players updated from local metrics.");
        }

        // Filter players that still need Wikipedia lookup (missing BirthDate or Height/Weight)
        var playersForWiki = allNeedingEnrichment
            .Where(p => p.BirthDate == null || p.HeightCm == null || p.WeightKg == null)
            .ToList();

        Console.WriteLine($"[3/3] Commencing high-throughput parallel Wikipedia/Wikidata ingestion for {playersForWiki.Count:N0} players...");

        var enrichedList = new ConcurrentBag<EnrichedPlayerEntryDto>();
        const int batchSize = 25;
        var batches = playersForWiki
            .Select((p, idx) => (Player: p, Index: idx))
            .GroupBy(x => x.Index / batchSize)
            .Select(g => g.Select(x => x.Player).ToList())
            .ToList();

        int completedBatches = 0;
        int totalBatches = batches.Count;
        using var semaphore = new SemaphoreSlim(2);

        var tasks = batches.Select(async batch =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var batchEnriched = await ProcessWikiBatchAsync(batch, cancellationToken);
                foreach (var item in batchEnriched)
                {
                    enrichedList.Add(item);
                }

                int done = Interlocked.Increment(ref completedBatches);
                int percent = (int)Math.Round((double)done / totalBatches * 100);
                Console.Write($"\r   -> Progress: [{done}/{totalBatches} batches] ({percent}%) | Enriched: {enrichedList.Count:N0} players   ");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine();

        // 3. Save all changes to PostgreSQL
        await _dbContext.SaveChangesAsync(cancellationToken);

        Console.WriteLine("\n=======================================================");
        Console.WriteLine($"  FULL DATABASE BIO ENRICHMENT COMPLETE!");
        Console.WriteLine($"  Total Processed: {totalFound:N0} players");
        Console.WriteLine($"  Total Successfully Enriched: {enrichedList.Count:N0} players");
        Console.WriteLine("=======================================================\n");

        int failed = Math.Max(0, totalFound - enrichedList.Count);
        return new BioEnrichmentSummaryDto(totalFound, enrichedList.Count, failed, enrichedList.ToList());
    }

    private async Task<List<EnrichedPlayerEntryDto>> EnrichPlayerBatchListAsync(List<Player> targetPlayers, CancellationToken cancellationToken)
    {
        var enrichedList = new List<EnrichedPlayerEntryDto>();
        const int batchSize = 50;

        for (int i = 0; i < targetPlayers.Count; i += batchSize)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var batch = targetPlayers.Skip(i).Take(batchSize).ToList();
            var batchEnriched = await ProcessWikiBatchAsync(batch, cancellationToken);
            enrichedList.AddRange(batchEnriched);
        }

        if (enrichedList.Any())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return enrichedList;
    }

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

                    // Guardrail: Ensure article actually refers to a basketball player / athlete
                    if ((!wikitext.Contains("basketball", StringComparison.OrdinalIgnoreCase) &&
                         !wikitext.Contains("NBA", StringComparison.OrdinalIgnoreCase) &&
                         !wikitext.Contains("EuroLeague", StringComparison.OrdinalIgnoreCase) &&
                         !wikitext.Contains("FIBA", StringComparison.OrdinalIgnoreCase) &&
                         !wikitext.Contains("infobox basketball biography", StringComparison.OrdinalIgnoreCase)) ||
                        (wikitext.Contains("Infobox football biography", StringComparison.OrdinalIgnoreCase) &&
                         !wikitext.Contains("Infobox basketball", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var bio = ParseWikitextBio(wikitext);
                    bool shouldUpdateNat = bio.Nationality != null &&
                        (string.IsNullOrWhiteSpace(player.Nationality) || player.Nationality == "INTL");

                    if (bio.BirthDate.HasValue || bio.HeightCm.HasValue || bio.WeightKg.HasValue || bio.Position != null || shouldUpdateNat)
                    {
                        player.UpdateDetails(
                            firstName: player.FirstName,
                            lastName: player.LastName,
                            position: bio.Position ?? player.Position,
                            height: bio.HeightStr ?? player.Height,
                            weightPounds: bio.WeightPounds ?? player.WeightPounds,
                            teamName: player.TeamName,
                            league: player.League,
                            nationality: shouldUpdateNat ? bio.Nationality : player.Nationality,
                            birthDate: bio.BirthDate ?? player.BirthDate,
                            heightCm: bio.HeightCm ?? player.HeightCm,
                            weightKg: bio.WeightKg ?? player.WeightKg,
                            wingspanCm: player.WingspanCm,
                            draftYear: bio.DraftYear ?? player.DraftYear,
                            draftPick: bio.DraftPick ?? player.DraftPick);

                        enriched.Add(new EnrichedPlayerEntryDto(
                            FullName: player.FullName,
                            League: player.League,
                            BirthDate: player.BirthDate?.ToString("yyyy-MM-dd"),
                            Age: player.CurrentAge,
                            HeightCm: player.HeightCm,
                            WeightKg: player.WeightKg,
                            Position: player.PrimaryPosition ?? player.Position,
                            DraftYear: player.DraftYear));
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
        ["yugoslavia"] = "SRB", ["yugoslav"] = "SRB"
    };

    private static string? ExtractNationality(string text)
    {
        var m = Regex.Match(text, @"\|\s*nationality\s*=\s*(.+?)(?:\r|\n)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        string raw = m.Groups[1].Value;
        raw = Regex.Replace(raw, @"<ref[^>]*>.*?</ref>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        raw = Regex.Replace(raw, @"<ref[^/>]*/?>", "", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, @"\{\{flag(?:u|icon)?\|([^}|]+)(?:\|[^}]*)?\}\}", "$1", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, @"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", "$1");
        raw = raw.Replace("{{", "").Replace("}}", "");

        var codes = new List<string>();
        foreach (var part in Regex.Split(raw, @"[/,]|\band\b|\|", RegexOptions.IgnoreCase))
        {
            var token = part.Trim(' ', '.', ':', ';', '[', ']', '*', '\t');
            if (CountryToIsoMap.TryGetValue(token, out var iso))
            {
                if (!codes.Contains(iso)) codes.Add(iso);
            }
            else if (Regex.IsMatch(token, @"^[A-Z]{3}$") && !codes.Contains(token))
            {
                codes.Add(token);
            }
        }

        if (codes.Count > 0)
            return string.Join(" / ", codes.Take(2));

        return null;
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
