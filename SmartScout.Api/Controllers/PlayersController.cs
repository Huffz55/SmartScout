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
        CancellationToken cancellationToken)
    {
        var player = await dbContext.Players.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (player == null) return NotFound();
        player.ToggleWatchlist();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { isWatchlisted = player.IsWatchlisted });
    }

    public record ScoutNotesDto(string? Notes);

    [HttpPost("{id:guid}/notes")]
    public async Task<IActionResult> SaveNotes(
        Guid id, 
        [FromBody] ScoutNotesDto request, 
        [FromServices] ISmartScoutDbContext dbContext, 
        CancellationToken cancellationToken)
    {
        var player = await dbContext.Players.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (player == null) return NotFound();
        player.SetScoutNotes(request?.Notes);
        await dbContext.SaveChangesAsync(cancellationToken);
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
}
