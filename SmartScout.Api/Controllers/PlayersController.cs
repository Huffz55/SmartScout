using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartScout.Api.Attributes;
using SmartScout.Application.Interfaces;
using SmartScout.Application.UseCases.Players;
using System;
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
}
