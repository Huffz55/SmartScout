using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartScout.Api.Attributes;
using SmartScout.Application.UseCases.Analytics;
using SmartScout.Application.UseCases.Injuries;
using SmartScout.Application.UseCases.Players;
using SmartScout.Application.UseCases.SeasonStats;
using SmartScout.Application.UseCases.Teams;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

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

    public record ScoutNotesDto(string? Notes);
    public record SeedDemoWatchlistRequest(Guid? BoardId = null);

    private static string FormatLeagueFilter(string? league) =>
        string.IsNullOrWhiteSpace(league) || league.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? "ALL (EuroLeague, EuroCup, ACB, BSL, BCL, ABA, BBL, LNB, LegaA, GBL, LKL, GLeague, VTB)"
            : league;

    [HttpPost("sync")]
    [ApiKeyAuth]
    [EnableRateLimiting("SyncPolicy")]
    public async Task<IActionResult> SyncPlayers(
        [FromQuery] int cursor = 0,
        [FromQuery] string? league = null,
        [FromQuery] string? season = null,
        [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new SyncPlayersCommand
        {
            Cursor = cursor,
            League = league,
            Season = season,
            Force = force
        }, cancellationToken);

        return Ok(new
        {
            Message = $"Successfully synced {count} players.",
            SyncedCount = count,
            LeagueFilter = FormatLeagueFilter(league),
            SeasonFilter = season ?? "ALL"
        });
    }

    [HttpPost("sync-stats")]
    [ApiKeyAuth]
    [EnableRateLimiting("SyncPolicy")]
    public async Task<IActionResult> SyncSeasonStats(
        [FromQuery] string? league = null,
        [FromQuery] string? season = null,
        [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new SyncSeasonStatsCommand
        {
            League = league,
            Season = season,
            Force = force
        }, cancellationToken);

        return Ok(new
        {
            Message = $"Successfully synced season stats for {count} stint records (Completed seasons are automatically checkpointed in IngestionJobLogs).",
            SyncedCount = count,
            LeagueFilter = FormatLeagueFilter(league),
            SeasonFilter = season ?? "ALL",
            ForceResync = force
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetPlayers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPlayersQuery
        {
            Page = page,
            PageSize = pageSize,
            SearchTerm = search
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("sleepers")]
    public async Task<IActionResult> GetSleeperPlayers(
        [FromQuery] string? category = null,
        [FromQuery] string? league = null,
        [FromQuery] int? season = null,
        [FromQuery] int? maxAge = null,
        [FromQuery] string? position = null,
        [FromQuery] int top = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetSleeperPlayersQuery
        {
            Category = category,
            League = league,
            Season = season,
            MaxAge = maxAge,
            Position = position,
            Top = top
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("smart-search")]
    public async Task<IActionResult> SmartSearch(
        [FromQuery] string prompt,
        [FromQuery] int? season = null,
        [FromQuery] int top = 15,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new SmartScoutSearchQuery
        {
            Prompt = prompt,
            Season = season,
            Top = top
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}/similar")]
    public async Task<IActionResult> GetSimilarPlayers(
        Guid id,
        [FromQuery] int? season = null,
        [FromQuery] int top = 5,
        [FromQuery] int minGamesPlayed = 3,
        [FromQuery] bool excludeSamePlayer = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetSimilarPlayersQuery
        {
            PlayerId = id,
            Season = season ?? 2026,
            Top = top,
            MinGamesPlayed = minGamesPlayed,
            ExcludeSamePlayer = excludeSamePlayer
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}/compare")]
    public async Task<IActionResult> ComparePlayers(
        Guid id,
        [FromQuery] int season,
        [FromQuery] Guid targetPlayerId,
        [FromQuery] int targetSeason,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ComparePlayersQuery
        {
            PlayerAId = id,
            SeasonA = season,
            PlayerBId = targetPlayerId,
            SeasonB = targetSeason
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}/report.pdf")]
    public async Task<IActionResult> DownloadScoutingReport(
        Guid id,
        [FromQuery] int season,
        CancellationToken cancellationToken = default)
    {
        byte[] pdfBytes = await _mediator.Send(new GetPlayerScoutingReportQuery
        {
            PlayerId = id,
            Season = season
        }, cancellationToken);

        return File(pdfBytes, "application/pdf", $"Scouting_Report_{id}_{season}.pdf");
    }

    [HttpGet("{id:guid}/compare.pdf")]
    public async Task<IActionResult> DownloadComparisonPdf(
        Guid id,
        [FromQuery] int season,
        [FromQuery] Guid targetPlayerId,
        [FromQuery] int targetSeason,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetComparisonReportPdfQuery(id, season, targetPlayerId, targetSeason), cancellationToken);
        return File(result.PdfBytes, "application/pdf", result.FileName);
    }

    [HttpGet("teams")]
    public async Task<IActionResult> GetTeamsSummary(
        [FromQuery] string? league = null,
        [FromQuery] int season = 2027,
        [FromQuery] string? search = null,
        [FromQuery] int top = 60,
        CancellationToken cancellationToken = default)
    {
        var grouped = await _mediator.Send(new GetTeamsSummaryQuery(league, season, search, top), cancellationToken);
        return Ok(grouped);
    }

    [HttpGet("teams/roster")]
    public async Task<IActionResult> GetTeamRoster(
        [FromQuery] string? league,
        [FromQuery] string team,
        [FromQuery] int? season = null,
        CancellationToken cancellationToken = default)
    {
        var roster = await _mediator.Send(new GetTeamRosterQuery(league, team, season), cancellationToken);
        return Ok(roster);
    }

    [HttpGet("{id:guid}/profile")]
    public async Task<IActionResult> GetPlayerProfile(
        Guid id,
        [FromQuery] int? season = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPlayerProfileQuery
        {
            PlayerId = id,
            Season = season
        }, cancellationToken);

        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost("enrich-bios")]
    [ApiKeyAuth]
    public async Task<IActionResult> EnrichBios(
        [FromQuery] string? league = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new EnrichPlayerBiosCommand(league, limit), cancellationToken);
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
    public async Task<IActionResult> SyncLiveInjuries(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new SyncLiveInjuriesCommand(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("sync-status")]
    public async Task<IActionResult> GetSyncStatus(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetSyncStatusQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("injuries")]
    public async Task<IActionResult> GetInjuriesSummary(
        [FromQuery] bool activeOnly = true,
        [FromQuery] int top = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetInjuriesSummaryQuery(activeOnly, top), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/watchlist")]
    public async Task<IActionResult> ToggleWatchlist(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ToggleWatchlistCommand(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<IActionResult> SaveNotes(
        Guid id,
        [FromBody] ScoutNotesDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new SavePlayerNotesCommand(id, request?.Notes), cancellationToken);
        return Ok(result);
    }

    [HttpGet("watchlist")]
    public async Task<IActionResult> GetWatchlist(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetWatchlistQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("watchlist/seed-demo")]
    public async Task<IActionResult> SeedDemoWatchlist(
        [FromQuery] Guid? boardId = null,
        CancellationToken cancellationToken = default)
    {
        Guid? resolvedBoardId = boardId;
        if (!resolvedBoardId.HasValue && HttpContext?.Request is { } req && req.ContentLength != 0 && req.HasJsonContentType())
        {
            try
            {
                using var reader = new StreamReader(req.Body, leaveOpen: true);
                string rawBody = await reader.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    var body = JsonSerializer.Deserialize<SeedDemoWatchlistRequest>(
                        rawBody,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    resolvedBoardId = body?.BoardId;
                }
            }
            catch (JsonException)
            {
                return BadRequest(new { Message = "Invalid JSON payload for boardId." });
            }
        }

        var result = await _mediator.Send(new SeedDemoWatchlistCommand(resolvedBoardId), cancellationToken);
        return Ok(result);
    }
}
