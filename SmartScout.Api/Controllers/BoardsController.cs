using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SmartScout.Application.UseCases.Boards;

namespace SmartScout.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BoardsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BoardsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    public record CreateBoardRequest(string Name, string? OwnerName = null);
    public record BoardNotesRequest(string? Notes);

    [HttpGet]
    public async Task<IActionResult> GetBoards(CancellationToken cancellationToken)
    {
        var boards = await _mediator.Send(new GetScoutBoardsQuery(), cancellationToken);
        return Ok(boards);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBoard(
        [FromBody] CreateBoardRequest request,
        CancellationToken cancellationToken)
    {
        var board = await _mediator.Send(new CreateScoutBoardCommand(request?.Name ?? string.Empty, request?.OwnerName), cancellationToken);
        return Ok(board);
    }

    [HttpDelete("{boardId:guid}")]
    public async Task<IActionResult> DeleteBoard(Guid boardId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteScoutBoardCommand(boardId), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{boardId:guid}/players")]
    public async Task<IActionResult> GetBoardPlayers(Guid boardId, CancellationToken cancellationToken)
    {
        var items = await _mediator.Send(new GetBoardPlayersQuery(boardId), cancellationToken);
        return Ok(items);
    }

    [HttpPost("{boardId:guid}/players/{playerId:guid}")]
    public async Task<IActionResult> TogglePlayerOnBoard(
        Guid boardId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new TogglePlayerOnBoardCommand(boardId, playerId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{boardId:guid}/players/{playerId:guid}/notes")]
    public async Task<IActionResult> SaveBoardPlayerNotes(
        Guid boardId,
        Guid playerId,
        [FromBody] BoardNotesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SaveBoardPlayerNotesCommand(boardId, playerId, request?.Notes), cancellationToken);
        return Ok(result);
    }
}
