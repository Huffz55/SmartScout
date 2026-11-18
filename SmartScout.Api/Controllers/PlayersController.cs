using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartScout.Api.Attributes;
using SmartScout.Application.Interfaces;
using SmartScout.Application.UseCases.Players;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Options;
using SmartScout.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlayersController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlayersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("sync")]
    [ApiKeyAuth]
    [EnableRateLimiting("SyncPolicy")]
    public async Task<IActionResult> SyncPlayers(
        [FromQuery] int cursor = 0,
        [FromQuery] string? league = null,
        [FromQuery] string? season = null,
        [FromQuery] bool force = false)
    {
        var command = new SyncPlayersCommand
        {
            Cursor = cursor,
            League = league,
            Season = season,
            Force = force
        };
        var count = await _mediator.Send(command);

        return Ok(new
        {
            Message = $"Successfully synced {count} players.",
            SyncedCount = count,
            LeagueFilter = string.IsNullOrWhiteSpace(league) || league.Equals("all", StringComparison.OrdinalIgnoreCase) 
                ? "ALL (EuroLeague, EuroCup, ACB, BSL, BCL, ABA, BBL, LNB, LegaA, GBL, LKL, GLeague, VTB)" 
                : league,
            SeasonFilter = season ?? "ALL"
        });
    }

    [HttpPost("sync-stats")]
    [ApiKeyAuth]
    [EnableRateLimiting("SyncPolicy")]
    public async Task<IActionResult> SyncSeasonStats(
        [FromQuery] string? league = null,
        [FromQuery] string? season = null,
        [FromQuery] bool force = false)
    {
        var command = new SmartScout.Application.UseCases.SeasonStats.SyncSeasonStatsCommand
        {
            League = league,
            Season = season,
            Force = force
        };
        var count = await _mediator.Send(command);

        return Ok(new
        {
            Message = $"Successfully synced season stats for {count} stint records (Completed seasons are automatically checkpointed in IngestionJobLogs).",
            SyncedCount = count,
            LeagueFilter = string.IsNullOrWhiteSpace(league) || league.Equals("all", StringComparison.OrdinalIgnoreCase) 
                ? "ALL (EuroLeague, EuroCup, ACB, BSL, BCL, ABA, BBL, LNB, LegaA, GBL, LKL, GLeague, VTB)" 
                : league,
            SeasonFilter = season ?? "ALL",
            ForceResync = force
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetPlayers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        var query = new GetPlayersQuery
        {
            Page = page,
            PageSize = pageSize,
            SearchTerm = search
        };
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    [HttpGet("sleepers")]
    public async Task<IActionResult> GetSleeperPlayers(
        [FromQuery] string? category = null,
        [FromQuery] string? league = null,
        [FromQuery] int? season = null,
        [FromQuery] int? maxAge = null,
        [FromQuery] string? position = null,
        [FromQuery] int top = 25)
    {
        var query = new SmartScout.Application.UseCases.Analytics.GetSleeperPlayersQuery
        {
            Category = category,
            League = league,
            Season = season,
            MaxAge = maxAge,
            Position = position,
            Top = top
        };
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    [HttpGet("smart-search")]
    public async Task<IActionResult> SmartSearch(
        [FromQuery] string prompt,
        [FromQuery] int? season = null,
        [FromQuery] int top = 15)
    {
        var query = new SmartScout.Application.UseCases.Analytics.SmartScoutSearchQuery
        {
            Prompt = prompt,
            Season = season,
            Top = top
        };
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    [HttpGet("{id:guid}/similar")]
    public async Task<IActionResult> GetSimilarPlayers(
        Guid id,
        [FromQuery] int? season = null,
        [FromQuery] int top = 5,
        [FromQuery] int minGamesPlayed = 3,
        [FromQuery] bool excludeSamePlayer = true)
    {
        var query = new SmartScout.Application.UseCases.Analytics.GetSimilarPlayersQuery
        {
            PlayerId = id,
            Season = season ?? 2026,
            Top = top,
            MinGamesPlayed = minGamesPlayed,
            ExcludeSamePlayer = excludeSamePlayer
        };
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    [HttpGet("{id:guid}/compare")]
    public async Task<IActionResult> ComparePlayers(
        Guid id,
        [FromQuery] int season,
        [FromQuery] Guid targetPlayerId,
        [FromQuery] int targetSeason)
    {
        var query = new SmartScout.Application.UseCases.Analytics.ComparePlayersQuery
        {
            PlayerAId = id,
            SeasonA = season,
            PlayerBId = targetPlayerId,
            SeasonB = targetSeason
        };
        var result = await _mediator.Send(query);

        return Ok(result);
    }

    [HttpGet("{id:guid}/report.pdf")]
    public async Task<IActionResult> DownloadScoutingReport(
        Guid id,
        [FromQuery] int season)
    {
        var query = new SmartScout.Application.UseCases.Analytics.GetPlayerScoutingReportQuery
        {
            PlayerId = id,
            Season = season
        };

        byte[] pdfBytes = await _mediator.Send(query);

        string fileName = $"Scouting_Report_{id}_{season}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpGet("{id:guid}/compare.pdf")]
    public async Task<IActionResult> DownloadComparisonPdf(
        Guid id,
        [FromQuery] int season,
        [FromQuery] Guid targetPlayerId,
        [FromQuery] int targetSeason,
        [FromServices] IScoutingReportGenerator reportGenerator)
    {
        var comparison = await _mediator.Send(new SmartScout.Application.UseCases.Analytics.ComparePlayersQuery
        {
            PlayerAId = id,
            SeasonA = season,
            PlayerBId = targetPlayerId,
            SeasonB = targetSeason
        });

        byte[] pdfBytes = reportGenerator.GenerateComparisonPdf(comparison);
        string safeNameA = (comparison.PlayerA.FullName ?? "PlayerA").Replace(" ", "_");
        string safeNameB = (comparison.PlayerB.FullName ?? "PlayerB").Replace(" ", "_");
        return File(pdfBytes, "application/pdf", $"SmartScout_Compare_{safeNameA}_vs_{safeNameB}.pdf");
    }

    [HttpGet("teams")]
    public async Task<IActionResult> GetTeamsSummary(
        [FromServices] ISmartScoutDbContext dbContext,
        [FromQuery] string? league = null,
        [FromQuery] int season = 2027,
        [FromQuery] string? search = null,
        [FromQuery] int top = 60,
        CancellationToken cancellationToken = default)
    {
        var statsQuery = dbContext.SeasonStats
            .AsNoTracking()
            .Include(s => s.Player)
            .Where(s => s.Season == season && !string.IsNullOrWhiteSpace(s.TeamName) && s.TeamName != "TOT" && s.TeamName != "2TM" && s.TeamName != "3TM");

        if (!string.IsNullOrWhiteSpace(league) && !string.Equals(league, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            statsQuery = statsQuery.Where(s => s.League == league.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string pattern = $"%{search.Trim()}%";
            statsQuery = statsQuery.Where(s => EF.Functions.ILike(s.TeamName!, pattern) || EF.Functions.ILike(s.League, pattern));
        }

        var rows = await statsQuery
            .Select(s => new
            {
                s.League,
                TeamName = s.TeamName!,
                s.PlayerId,
                Age = s.Player != null ? s.Player.CurrentAge : null,
                s.EuroLeagueTransitionIndex,
                s.Points
            })
            .ToListAsync(cancellationToken);

        var activeInjuredIds = await dbContext.PlayerInjuries
            .AsNoTracking()
            .Where(i => i.Status != "Recovered" && i.Status != "Healthy" && i.Status != "Cleared")
            .Select(i => i.PlayerId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var injuredSet = activeInjuredIds.ToHashSet();

        var grouped = rows
            .GroupBy(r => (r.League, r.TeamName))
            .Select(g => new
            {
                League = g.Key.League,
                TeamName = g.Key.TeamName,
                Season = season,
                PlayerCount = g.Select(x => x.PlayerId).Distinct().Count(),
                AvgAge = g.Where(x => x.Age.HasValue).Select(x => (double)x.Age!.Value).DefaultIfEmpty(0).Average(),
                AvgEti = g.Where(x => x.EuroLeagueTransitionIndex.HasValue).Select(x => (double)x.EuroLeagueTransitionIndex!.Value).DefaultIfEmpty(0).Average(),
                AvgPoints = g.Where(x => x.Points.HasValue).Select(x => (double)x.Points!.Value).DefaultIfEmpty(0).Average(),
                ActiveInjuriesCount = g.Select(x => x.PlayerId).Distinct().Count(id => injuredSet.Contains(id))
            })
            .OrderByDescending(x => x.AvgEti)
            .ThenByDescending(x => x.PlayerCount)
            .Take(Math.Clamp(top, 1, 200))
            .ToList();

        return Ok(grouped);
    }

    [HttpGet("teams/roster")]
    public async Task<IActionResult> GetTeamRoster(
        [FromServices] ISmartScoutDbContext dbContext,
        [FromQuery] string league,
        [FromQuery] string team,
        [FromQuery] int? season = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(team))
            return BadRequest(new { Message = "Team parameter is required." });

        var lowerVariants = SmartScout.Application.Services.SleeperScoutingService
            .GetTeamNameVariants(team)
            .Select(v => v.ToLowerInvariant())
            .ToList();

        int? resolvedMaxSeason = await dbContext.SeasonStats
            .AsNoTracking()
            .Where(s => s.TeamName != null
                && lowerVariants.Contains(s.TeamName.ToLower())
                && (string.IsNullOrWhiteSpace(league) || s.League == league))
            .MaxAsync(s => (int?)s.Season, cancellationToken);

        int targetSeason = season ?? resolvedMaxSeason ?? 2027;

        async Task<List<RosterProjection>> QuerySeasonRosterAsync(int querySeason)
        {
            var q = dbContext.SeasonStats
                .AsNoTracking()
                .Include(s => s.Player)
                .Where(s => s.Season == querySeason
                    && s.TeamName != null
                    && lowerVariants.Contains(s.TeamName.ToLower()));

            if (!string.IsNullOrWhiteSpace(league))
                q = q.Where(s => s.League == league);

            return await q
                .OrderByDescending(s => s.EuroLeagueTransitionIndex ?? 0m)
                .ThenByDescending(s => s.Points ?? 0m)
                .Select(s => new RosterProjection(
                    s.PlayerId,
                    s.Player != null ? s.Player.FullName : "Unknown",
                    s.Player != null ? (s.Player.PrimaryPosition ?? s.Player.Position) : null,
                    s.Player != null ? s.Player.Nationality : null,
                    s.Player != null ? s.Player.CurrentAge : null,
                    s.Player != null ? s.Player.HeightCm : null,
                    s.League,
                    s.TeamName!,
                    s.Season,
                    s.GamesPlayed,
                    s.Minutes,
                    s.Points,
                    s.Rebounds,
                    s.Assists,
                    s.PtsPer36,
                    s.TrueShootingPct,
                    s.Fg3Pct,
                    s.EuroLeagueTransitionIndex))
                .ToListAsync(cancellationToken);
        }

        var rawRoster = await QuerySeasonRosterAsync(targetSeason);

        // If caller explicitly passed a season with no matches, fall back to latest tracked season for this club
        if (rawRoster.Count == 0 && season.HasValue && resolvedMaxSeason.HasValue && resolvedMaxSeason.Value != targetSeason)
        {
            targetSeason = resolvedMaxSeason.Value;
            rawRoster = await QuerySeasonRosterAsync(targetSeason);
        }

        var roster = rawRoster
            .GroupBy(r => r.PlayerId)
            .Select(g => g.First())
            .ToList();

        var rosterIds = roster.Select(r => r.PlayerId).Distinct().ToList();
        var injuries = await dbContext.PlayerInjuries
            .AsNoTracking()
            .Where(i => rosterIds.Contains(i.PlayerId) && i.Status != "Recovered" && i.Status != "Healthy" && i.Status != "Cleared")
            .OrderByDescending(i => i.ReportedAt)
            .ToListAsync(cancellationToken);

        var injByPlayer = injuries.GroupBy(i => i.PlayerId).ToDictionary(g => g.Key, g => g.First());

        var enrichedRoster = roster.Select(r => new
        {
            r.PlayerId,
            r.FullName,
            r.Position,
            r.Nationality,
            r.Age,
            r.HeightCm,
            r.League,
            r.TeamName,
            r.Season,
            r.GamesPlayed,
            r.Minutes,
            r.Points,
            r.Rebounds,
            r.Assists,
            r.PtsPer36,
            r.TrueShootingPct,
            r.Fg3Pct,
            r.EuroLeagueTransitionIndex,
            ActiveInjuryStatus = injByPlayer.TryGetValue(r.PlayerId, out var inj) ? inj.Status : null,
            ActiveInjuryDetail = injByPlayer.TryGetValue(r.PlayerId, out var inj2) ? inj2.InjuryType : null
        }).ToList();

        return Ok(new
        {
            TeamName = team,
            League = league,
            Season = targetSeason,
            PlayerCount = enrichedRoster.Count,
            ActiveInjuriesCount = enrichedRoster.Count(r => r.ActiveInjuryStatus != null),
            Roster = enrichedRoster
        });
    }

    [HttpGet("{id:guid}/profile")]
    public async Task<IActionResult> GetPlayerProfile(
        Guid id,
        [FromQuery] int? season = null)
    {
        var query = new SmartScout.Application.UseCases.Analytics.GetPlayerProfileQuery
        {
            PlayerId = id,
            Season = season
        };
        var result = await _mediator.Send(query);
        if (result == null)
            return NotFound();

        return Ok(result);
    }

    [HttpPost("enrich-bios")]
    [ApiKeyAuth]
    public async Task<IActionResult> EnrichBios(
        [FromServices] IPlayerBioEnrichmentService enrichmentService,
        [FromQuery] string? league = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await enrichmentService.EnrichPlayerBiosAsync(league, limit, cancellationToken);
        return Ok(new
        {
            Message = $"Enrichment cycle completed: {result.TotalEnriched} of {result.TotalProcessed} players enriched with biographical and physical metrics.",
            result.TotalProcessed,
            result.TotalEnriched,
            result.TotalFailed,
            result.EnrichedPlayers
        });
    }

    [HttpPost("sync-injuries")]
    [ApiKeyAuth]
    [EnableRateLimiting("SyncPolicy")]
    public async Task<IActionResult> SyncLiveInjuries(
        [FromServices] IPlayerInjurySyncService injurySyncService,
        [FromServices] ISmartScoutDbContext dbContext,
        [FromServices] IOptions<ScraperOptions> scraperOptions,
        CancellationToken cancellationToken = default)
    {
        var result = await injurySyncService.SyncLiveInjuriesAsync(cancellationToken);

        int liveSeason = scraperOptions.Value.LiveSeasonYear;
        var injuryLog = await dbContext.IngestionJobLogs
            .FirstOrDefaultAsync(l => l.LeagueCode == LiveSyncCoordinator.InjuryCheckpointCode && l.Season == liveSeason, cancellationToken);

        if (injuryLog == null)
        {
            injuryLog = new IngestionJobLog(LiveSyncCoordinator.InjuryCheckpointCode, liveSeason, "Completed", result.MatchedPlayers);
            await dbContext.IngestionJobLogs.AddAsync(injuryLog, cancellationToken);
        }
        else
        {
            injuryLog.MarkCompleted(result.MatchedPlayers);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            Message = $"Live injury sync completed: {result.MatchedPlayers} matched players ({result.InsertedCount} inserted, {result.UpdatedCount} updated, {result.RecoveredMarkedCount} recovered).",
            result.TotalFetched,
            result.MatchedPlayers,
            result.InsertedCount,
            result.UpdatedCount,
            result.RecoveredMarkedCount,
            result.SourceCounts,
            result.SampleMatched
        });
    }

    [HttpGet("sync-status")]
    public async Task<IActionResult> GetSyncStatus(
        [FromServices] ISmartScoutDbContext dbContext,
        [FromServices] IOptions<ScraperOptions> scraperOptions,
        CancellationToken cancellationToken = default)
    {
        var opts = scraperOptions.Value;

        var lastInjuryCheckpoint = await dbContext.IngestionJobLogs
            .AsNoTracking()
            .Where(l => l.LeagueCode == LiveSyncCoordinator.InjuryCheckpointCode && l.Status == "Completed")
            .OrderByDescending(l => l.UpdatedAt)
            .Select(l => (DateTime?)l.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var lastInjuryReportedAt = await dbContext.PlayerInjuries
            .AsNoTracking()
            .MaxAsync(i => (DateTime?)i.ReportedAt, cancellationToken);

        var lastSeasonStatSyncAt = await dbContext.IngestionJobLogs
            .AsNoTracking()
            .Where(l => l.Season == opts.LiveSeasonYear
                && l.LeagueCode != LiveSyncCoordinator.InjuryCheckpointCode
                && l.Status == "Completed")
            .MaxAsync(l => (DateTime?)l.UpdatedAt, cancellationToken);

        int activeInjuriesCount = await dbContext.PlayerInjuries
            .AsNoTracking()
            .CountAsync(i => i.Status != "Recovered" && i.Status != "Healthy" && i.Status != "Cleared", cancellationToken);

        int totalPlayersCount = await dbContext.Players
            .AsNoTracking()
            .CountAsync(cancellationToken);

        int liveSeasonStatRowsCount = await dbContext.SeasonStats
            .AsNoTracking()
            .CountAsync(s => s.Season == opts.LiveSeasonYear, cancellationToken);

        return Ok(new
        {
            opts.EnableBackgroundSync,
            opts.EnableLiveInjuryAutoSync,
            opts.InjurySyncIntervalHours,
            opts.CurrentSeasonStatRefreshHours,
            opts.LiveSeasonYear,
            LastInjurySyncAt = lastInjuryCheckpoint ?? lastInjuryReportedAt,
            LastSeasonStatSyncAt = lastSeasonStatSyncAt,
            ActiveInjuriesCount = activeInjuriesCount,
            TotalPlayersCount = totalPlayersCount,
            LiveSeasonStatRowsCount = liveSeasonStatRowsCount
        });
    }

    [HttpGet("injuries")]
    public async Task<IActionResult> GetInjuriesSummary(
        [FromServices] ISmartScoutDbContext dbContext,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int top = 100,
        CancellationToken cancellationToken = default)
    {
        int clampedTop = Math.Clamp(top, 1, 500);
        var query = dbContext.PlayerInjuries
            .AsNoTracking()
            .Include(i => i.Player)
            .AsQueryable();

        if (activeOnly)
        {
            query = query.Where(i => i.Status != "Recovered" && i.Status != "Healthy" && i.Status != "Cleared");
        }

        var items = await query
            .OrderByDescending(i => i.ReportedAt)
            .Take(clampedTop)
            .Select(i => new
            {
                i.Id,
                i.PlayerId,
                PlayerName = i.Player != null ? i.Player.FullName : "Unknown",
                League = i.Player != null ? i.Player.League : "-",
                TeamName = i.Player != null ? i.Player.TeamName : null,
                i.Season,
                i.InjuryType,
                i.Status,
                i.GamesMissed,
                i.Source,
                i.Notes,
                i.ReportedAt
            })
            .ToListAsync(cancellationToken);

        int totalCount = await dbContext.PlayerInjuries.CountAsync(cancellationToken);
        int activeCount = await dbContext.PlayerInjuries.CountAsync(
            i => i.Status != "Recovered" && i.Status != "Healthy" && i.Status != "Cleared",
            cancellationToken);

        return Ok(new
        {
            TotalRecords = totalCount,
            ActiveRecords = activeCount,
            Items = items
        });
    }

    [HttpPost("{id:guid}/watchlist")]
    public async Task<IActionResult> ToggleWatchlist(
        Guid id, 
        [FromServices] ISmartScoutDbContext dbContext,
        [FromServices] IScoutingCacheState? cacheState = null,
        CancellationToken cancellationToken = default)
    {
        var player = await dbContext.Players.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (player == null) return NotFound();
        player.ToggleWatchlist();
        await dbContext.SaveChangesAsync(cancellationToken);
        cacheState?.BumpVersion();
        return Ok(new { isWatchlisted = player.IsWatchlisted });
    }

    public record ScoutNotesDto(string? Notes);

    [HttpPost("{id:guid}/notes")]
    public async Task<IActionResult> SaveNotes(
        Guid id, 
        [FromBody] ScoutNotesDto request, 
        [FromServices] ISmartScoutDbContext dbContext,
        [FromServices] IScoutingCacheState? cacheState = null,
        CancellationToken cancellationToken = default)
    {
        var player = await dbContext.Players.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (player == null) return NotFound();
        player.SetScoutNotes(request?.Notes);
        await dbContext.SaveChangesAsync(cancellationToken);
        cacheState?.BumpVersion();
        return Ok(new { scoutNotes = player.ScoutNotes });
    }

    [HttpGet("watchlist")]
    public async Task<IActionResult> GetWatchlist(
        [FromServices] ISmartScoutDbContext dbContext, 
        CancellationToken cancellationToken)
    {
        var watchlisted = await dbContext.Players
            .Where(p => p.IsWatchlisted)
            .OrderBy(p => p.FullName)
            .Select(p => new
            {
                p.Id,
                p.FullName,
                p.PrimaryPosition,
                p.Position,
                p.HeightCm,
                p.Height,
                p.WeightKg,
                p.League,
                p.TeamName,
                p.Nationality,
                Age = p.CurrentAge,
                p.ScoutNotes,
                p.EstimatedMarketValueTier,
                p.ContractExpirySeason,
                LatestStat = p.SeasonStats.OrderByDescending(s => s.Season).Select(s => new
                {
                    s.Season,
                    s.Points,
                    s.Rebounds,
                    s.Assists,
                    s.Minutes,
                    s.TrueShootingPct,
                    s.EuroLeagueTransitionIndex,
                    s.StocksPer36
                }).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return Ok(watchlisted);
    }

    [HttpPost("watchlist/seed-demo")]
    public async Task<IActionResult> SeedDemoWatchlist(
        [FromServices] ISmartScoutDbContext dbContext,
        [FromServices] IOptions<ScraperOptions> scraperOptions,
        [FromServices] IScoutingCacheState? cacheState = null,
        CancellationToken cancellationToken = default)
    {
        int existingCount = await dbContext.Players.CountAsync(p => p.IsWatchlisted, cancellationToken);
        if (existingCount > 0)
        {
            return Ok(new
            {
                AddedCount = 0,
                TotalWatchlisted = existingCount,
                Players = Array.Empty<object>()
            });
        }

        int minSeason = scraperOptions.Value.LiveSeasonYear - 1;
        var candidates = await dbContext.Players
            .Include(p => p.SeasonStats)
            .Where(p => !p.IsWatchlisted
                && p.BirthDate.HasValue
                && !string.IsNullOrWhiteSpace(p.Nationality)
                && p.Nationality != "INTL"
                && p.SeasonStats.Any(s => s.Season >= minSeason && s.GamesPlayed >= 10 && s.EuroLeagueTransitionIndex.HasValue))
            .OrderByDescending(p => p.SeasonStats
                .Where(s => s.Season >= minSeason)
                .Max(s => s.EuroLeagueTransitionIndex) ?? 0m)
            .Take(120)
            .ToListAsync(cancellationToken);

        var selected = SelectDiverseDemoTargets(candidates, maxCount: 6);
        foreach (var player in selected)
        {
            player.ToggleWatchlist();
        }

        if (selected.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            cacheState?.BumpVersion();
        }

        return Ok(new
        {
            AddedCount = selected.Count,
            TotalWatchlisted = selected.Count,
            Players = selected.Select(p => new
            {
                p.Id,
                p.FullName,
                p.League,
                p.PrimaryPosition,
                p.Nationality
            })
        });
    }

    internal static List<Player> SelectDiverseDemoTargets(IEnumerable<Player> candidates, int maxCount = 6)
    {
        var ranked = candidates
            .OrderByDescending(p => p.SeasonStats
                .OrderByDescending(s => s.Season)
                .Select(s => s.EuroLeagueTransitionIndex ?? 0m)
                .FirstOrDefault())
            .ThenBy(p => p.CurrentAge ?? 30)
            .ThenBy(p => p.FullName)
            .ToList();

        var chosen = new List<Player>();
        var usedIds = new HashSet<Guid>();
        var usedLeagues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string BucketPos(Player p)
        {
            var pos = (p.PrimaryPosition ?? p.Position ?? string.Empty).ToUpperInvariant();
            if (pos.StartsWith("C")) return "C";
            if (pos.Contains("PG") || pos.Contains("SG") || pos == "G" || pos == "G-F") return "G";
            return "F";
        }

        foreach (string bucket in new[] { "G", "F", "C" })
        {
            var pick = ranked.FirstOrDefault(p =>
                !usedIds.Contains(p.Id)
                && BucketPos(p) == bucket
                && !usedLeagues.Contains(p.League ?? string.Empty));

            pick ??= ranked.FirstOrDefault(p => !usedIds.Contains(p.Id) && BucketPos(p) == bucket);
            if (pick != null && chosen.Count < maxCount)
            {
                chosen.Add(pick);
                usedIds.Add(pick.Id);
                if (!string.IsNullOrWhiteSpace(pick.League))
                    usedLeagues.Add(pick.League);
            }
        }

        foreach (var player in ranked)
        {
            if (chosen.Count >= maxCount) break;
            if (usedIds.Contains(player.Id)) continue;
            if (!string.IsNullOrWhiteSpace(player.League) && usedLeagues.Contains(player.League) && ranked.Any(r => !usedIds.Contains(r.Id) && !usedLeagues.Contains(r.League ?? string.Empty)))
                continue;

            chosen.Add(player);
            usedIds.Add(player.Id);
            if (!string.IsNullOrWhiteSpace(player.League))
                usedLeagues.Add(player.League);
        }

        return chosen;
    }

    private sealed record RosterProjection(
        Guid PlayerId,
        string FullName,
        string? Position,
        string? Nationality,
        int? Age,
        int? HeightCm,
        string League,
        string TeamName,
        int Season,
        int GamesPlayed,
        decimal? Minutes,
        decimal? Points,
        decimal? Rebounds,
        decimal? Assists,
        decimal? PtsPer36,
        decimal? TrueShootingPct,
        decimal? Fg3Pct,
        decimal? EuroLeagueTransitionIndex);
}
