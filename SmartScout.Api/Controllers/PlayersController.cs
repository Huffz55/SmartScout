using MediatR;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<IActionResult> SyncPlayers([FromQuery] int cursor = 0)
    {
        var command = new SyncPlayersCommand { Cursor = cursor };
        var count = await _mediator.Send(command);

        return Ok(new { Message = $"Successfully synced {count} players.", SyncedCount = count });
    }

    [HttpGet]
    public async Task<IActionResult> GetPlayers()
    {
        var query = new GetPlayersQuery();
        var players = await _mediator.Send(query);

        return Ok(players);
    }
}
