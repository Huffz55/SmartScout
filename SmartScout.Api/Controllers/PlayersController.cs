using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartScout.Api.Attributes;
using SmartScout.Application.UseCases.Players;
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
    public async Task<IActionResult> SyncPlayers([FromQuery] int cursor = 0)
    {
        var command = new SyncPlayersCommand { Cursor = cursor };
        var count = await _mediator.Send(command);

        return Ok(new { Message = $"Successfully synced {count} players.", SyncedCount = count });
    }

    [HttpPost("sync-stats")]
    [ApiKeyAuth]
    [EnableRateLimiting("SyncPolicy")]
    public async Task<IActionResult> SyncSeasonStats()
    {
        var command = new SmartScout.Application.UseCases.SeasonStats.SyncSeasonStatsCommand();
        var count = await _mediator.Send(command);

        return Ok(new { Message = $"Successfully synced season stats for {count} records.", SyncedCount = count });
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
        [FromQuery] int season,
        [FromQuery] int top = 5,
        [FromQuery] int minGamesPlayed = 20,
        [FromQuery] bool excludeSamePlayer = true)
    {
        var query = new SmartScout.Application.UseCases.Analytics.GetSimilarPlayersQuery
        {
            PlayerId = id,
            Season = season,
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
}
