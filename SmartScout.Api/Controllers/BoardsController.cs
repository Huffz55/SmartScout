using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BoardsController : ControllerBase
{
    public record CreateBoardRequest(string Name, string? OwnerName = null);
    public record BoardNotesRequest(string? Notes);

    [HttpGet]
    public async Task<IActionResult> GetBoards(
        [FromServices] ISmartScoutDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var boards = await dbContext.ScoutBoards
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id,
                b.Name,
                b.OwnerName,
                b.CreatedAt,
                PlayerCount = b.Entries.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(boards);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBoard(
        [FromBody] CreateBoardRequest request,
        [FromServices] ISmartScoutDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            return BadRequest(new { Message = "Board name is required." });

        var board = new ScoutBoard(request.Name, request.OwnerName);
        await dbContext.ScoutBoards.AddAsync(board, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            board.Id,
            board.Name,
            board.OwnerName,
            board.CreatedAt,
            PlayerCount = 0
        });
    }

    [HttpDelete("{boardId:guid}")]
    public async Task<IActionResult> DeleteBoard(
        Guid boardId,
        [FromServices] ISmartScoutDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var board = await dbContext.ScoutBoards.FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);
        if (board == null) return NotFound();

        dbContext.ScoutBoards.Remove(board);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { deleted = true });
    }

    [HttpGet("{boardId:guid}/players")]
    public async Task<IActionResult> GetBoardPlayers(
        Guid boardId,
        [FromServices] ISmartScoutDbContext dbContext,
        CancellationToken cancellationToken)
    {
        bool exists = await dbContext.ScoutBoards.AnyAsync(b => b.Id == boardId, cancellationToken);
        if (!exists) return NotFound();

        var items = await dbContext.ScoutBoardEntries
            .AsNoTracking()
            .Where(e => e.BoardId == boardId && e.Player != null)
            .OrderBy(e => e.Player!.FullName)
            .Select(e => new
            {
                Id = e.Player!.Id,
                e.Player.FullName,
                e.Player.PrimaryPosition,
                e.Player.Position,
                e.Player.HeightCm,
                e.Player.Height,
                e.Player.WeightKg,
                e.Player.League,
                e.Player.TeamName,
                e.Player.Nationality,
                Age = e.Player.CurrentAge,
                ScoutNotes = e.Notes,
                e.Player.EstimatedMarketValueTier,
                e.Player.ContractExpirySeason,
                LatestStat = e.Player.SeasonStats.OrderByDescending(s => s.Season).Select(s => new
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

        return Ok(items);
    }

    [HttpPost("{boardId:guid}/players/{playerId:guid}")]
    public async Task<IActionResult> TogglePlayerOnBoard(
        Guid boardId,
        Guid playerId,
        [FromServices] ISmartScoutDbContext dbContext,
        CancellationToken cancellationToken)
    {
        bool boardExists = await dbContext.ScoutBoards.AnyAsync(b => b.Id == boardId, cancellationToken);
        bool playerExists = await dbContext.Players.AnyAsync(p => p.Id == playerId, cancellationToken);
        if (!boardExists || !playerExists) return NotFound();

        var existing = await dbContext.ScoutBoardEntries
            .FirstOrDefaultAsync(e => e.BoardId == boardId && e.PlayerId == playerId, cancellationToken);

        bool isWatchlisted;
        if (existing != null)
        {
            dbContext.ScoutBoardEntries.Remove(existing);
            isWatchlisted = false;
        }
        else
        {
            await dbContext.ScoutBoardEntries.AddAsync(new ScoutBoardEntry(boardId, playerId), cancellationToken);
            isWatchlisted = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { isWatchlisted });
    }

    [HttpPost("{boardId:guid}/players/{playerId:guid}/notes")]
    public async Task<IActionResult> SaveBoardPlayerNotes(
        Guid boardId,
        Guid playerId,
        [FromBody] BoardNotesRequest request,
        [FromServices] ISmartScoutDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.ScoutBoardEntries
            .FirstOrDefaultAsync(e => e.BoardId == boardId && e.PlayerId == playerId, cancellationToken);

        if (entry == null) return NotFound();
        entry.SetNotes(request?.Notes);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { scoutNotes = entry.Notes });
    }
}
