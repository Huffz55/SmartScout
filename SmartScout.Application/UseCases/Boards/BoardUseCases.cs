using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.UseCases.Boards;

public sealed record ScoutBoardSummaryDto(
    Guid Id,
    string Name,
    string OwnerName,
    DateTime CreatedAt,
    int PlayerCount);

public sealed record BoardPlayerLatestStatDto(
    int Season,
    decimal? Points,
    decimal? Rebounds,
    decimal? Assists,
    decimal? Minutes,
    decimal? TrueShootingPct,
    decimal? EuroLeagueTransitionIndex,
    decimal? StocksPer36);

public sealed record BoardPlayerDto(
    Guid Id,
    string FullName,
    string? PrimaryPosition,
    string? Position,
    int? HeightCm,
    string? Height,
    int? WeightKg,
    string League,
    string? TeamName,
    string? Nationality,
    int? Age,
    string? ScoutNotes,
    string? EstimatedMarketValueTier,
    int? ContractExpirySeason,
    BoardPlayerLatestStatDto? LatestStat);

public sealed record BoardDeleteResultDto(bool Deleted);
public sealed record BoardToggleResultDto(bool IsWatchlisted);
public sealed record BoardNotesResultDto(string? ScoutNotes);

public record GetScoutBoardsQuery : IRequest<List<ScoutBoardSummaryDto>>;

public class GetScoutBoardsQueryHandler : IRequestHandler<GetScoutBoardsQuery, List<ScoutBoardSummaryDto>>
{
    private readonly ISmartScoutDbContext _dbContext;

    public GetScoutBoardsQueryHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ScoutBoardSummaryDto>> Handle(GetScoutBoardsQuery request, CancellationToken cancellationToken)
    {
        return await _dbContext.ScoutBoards
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new ScoutBoardSummaryDto(
                b.Id,
                b.Name,
                b.OwnerName,
                b.CreatedAt,
                b.Entries.Count))
            .ToListAsync(cancellationToken);
    }
}

public record CreateScoutBoardCommand(string Name, string? OwnerName = null) : IRequest<ScoutBoardSummaryDto>;

public class CreateScoutBoardCommandValidator : AbstractValidator<CreateScoutBoardCommand>
{
    public CreateScoutBoardCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Board name is required.")
            .MaximumLength(100).WithMessage("Board name cannot exceed 100 characters.");

        RuleFor(x => x.OwnerName)
            .MaximumLength(80).WithMessage("Owner name cannot exceed 80 characters.");
    }
}

public class CreateScoutBoardCommandHandler : IRequestHandler<CreateScoutBoardCommand, ScoutBoardSummaryDto>
{
    private readonly ISmartScoutDbContext _dbContext;

    public CreateScoutBoardCommandHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ScoutBoardSummaryDto> Handle(CreateScoutBoardCommand request, CancellationToken cancellationToken)
    {
        var board = new ScoutBoard(request.Name, request.OwnerName);
        await _dbContext.ScoutBoards.AddAsync(board, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ScoutBoardSummaryDto(board.Id, board.Name, board.OwnerName, board.CreatedAt, 0);
    }
}

public record DeleteScoutBoardCommand(Guid BoardId) : IRequest<BoardDeleteResultDto>;

public class DeleteScoutBoardCommandValidator : AbstractValidator<DeleteScoutBoardCommand>
{
    public DeleteScoutBoardCommandValidator()
    {
        RuleFor(x => x.BoardId).NotEmpty();
    }
}

public class DeleteScoutBoardCommandHandler : IRequestHandler<DeleteScoutBoardCommand, BoardDeleteResultDto>
{
    private readonly ISmartScoutDbContext _dbContext;

    public DeleteScoutBoardCommandHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BoardDeleteResultDto> Handle(DeleteScoutBoardCommand request, CancellationToken cancellationToken)
    {
        var board = await _dbContext.ScoutBoards.FirstOrDefaultAsync(b => b.Id == request.BoardId, cancellationToken);
        if (board == null)
        {
            throw new KeyNotFoundException($"Scout board '{request.BoardId}' was not found.");
        }

        _dbContext.ScoutBoards.Remove(board);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new BoardDeleteResultDto(true);
    }
}

public record GetBoardPlayersQuery(Guid BoardId) : IRequest<List<BoardPlayerDto>>;

public class GetBoardPlayersQueryValidator : AbstractValidator<GetBoardPlayersQuery>
{
    public GetBoardPlayersQueryValidator()
    {
        RuleFor(x => x.BoardId).NotEmpty();
    }
}

public class GetBoardPlayersQueryHandler : IRequestHandler<GetBoardPlayersQuery, List<BoardPlayerDto>>
{
    private readonly ISmartScoutDbContext _dbContext;

    public GetBoardPlayersQueryHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<BoardPlayerDto>> Handle(GetBoardPlayersQuery request, CancellationToken cancellationToken)
    {
        bool exists = await _dbContext.ScoutBoards.AnyAsync(b => b.Id == request.BoardId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException($"Scout board '{request.BoardId}' was not found.");
        }

        var rows = await _dbContext.ScoutBoardEntries
            .AsNoTracking()
            .Where(e => e.BoardId == request.BoardId && e.Player != null)
            .OrderBy(e => e.Player!.FullName)
            .Select(e => new
            {
                e.Player!.Id,
                e.Player.FullName,
                e.Player.PrimaryPosition,
                e.Player.Position,
                e.Player.HeightCm,
                e.Player.Height,
                e.Player.WeightKg,
                e.Player.League,
                e.Player.TeamName,
                e.Player.Nationality,
                e.Player.BirthDate,
                e.Notes,
                e.Player.EstimatedMarketValueTier,
                e.Player.ContractExpirySeason,
                LatestStat = e.Player.SeasonStats
                    .OrderByDescending(s => s.Season)
                    .Select(s => new BoardPlayerLatestStatDto(
                        s.Season,
                        s.Points,
                        s.Rebounds,
                        s.Assists,
                        s.Minutes,
                        s.TrueShootingPct,
                        s.EuroLeagueTransitionIndex,
                        s.StocksPer36))
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new BoardPlayerDto(
            r.Id,
            r.FullName,
            r.PrimaryPosition,
            r.Position,
            r.HeightCm,
            r.Height,
            r.WeightKg,
            r.League,
            r.TeamName,
            r.Nationality,
            Player.CalculateAgeFromBirthDate(r.BirthDate),
            r.Notes,
            r.EstimatedMarketValueTier,
            r.ContractExpirySeason,
            r.LatestStat)).ToList();
    }
}

public record TogglePlayerOnBoardCommand(Guid BoardId, Guid PlayerId) : IRequest<BoardToggleResultDto>;

public class TogglePlayerOnBoardCommandValidator : AbstractValidator<TogglePlayerOnBoardCommand>
{
    public TogglePlayerOnBoardCommandValidator()
    {
        RuleFor(x => x.BoardId).NotEmpty();
        RuleFor(x => x.PlayerId).NotEmpty();
    }
}

public class TogglePlayerOnBoardCommandHandler : IRequestHandler<TogglePlayerOnBoardCommand, BoardToggleResultDto>
{
    private readonly ISmartScoutDbContext _dbContext;

    public TogglePlayerOnBoardCommandHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BoardToggleResultDto> Handle(TogglePlayerOnBoardCommand request, CancellationToken cancellationToken)
    {
        bool boardExists = await _dbContext.ScoutBoards.AnyAsync(b => b.Id == request.BoardId, cancellationToken);
        bool playerExists = await _dbContext.Players.AnyAsync(p => p.Id == request.PlayerId, cancellationToken);
        if (!boardExists || !playerExists)
        {
            throw new KeyNotFoundException("Board or player was not found.");
        }

        var existing = await _dbContext.ScoutBoardEntries
            .FirstOrDefaultAsync(e => e.BoardId == request.BoardId && e.PlayerId == request.PlayerId, cancellationToken);

        bool isWatchlisted;
        if (existing != null)
        {
            _dbContext.ScoutBoardEntries.Remove(existing);
            isWatchlisted = false;
        }
        else
        {
            await _dbContext.ScoutBoardEntries.AddAsync(new ScoutBoardEntry(request.BoardId, request.PlayerId), cancellationToken);
            isWatchlisted = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new BoardToggleResultDto(isWatchlisted);
    }
}

public record SaveBoardPlayerNotesCommand(Guid BoardId, Guid PlayerId, string? Notes) : IRequest<BoardNotesResultDto>;

public class SaveBoardPlayerNotesCommandValidator : AbstractValidator<SaveBoardPlayerNotesCommand>
{
    public SaveBoardPlayerNotesCommandValidator()
    {
        RuleFor(x => x.BoardId).NotEmpty();
        RuleFor(x => x.PlayerId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000).WithMessage("Notes cannot exceed 2000 characters.");
    }
}

public class SaveBoardPlayerNotesCommandHandler : IRequestHandler<SaveBoardPlayerNotesCommand, BoardNotesResultDto>
{
    private readonly ISmartScoutDbContext _dbContext;

    public SaveBoardPlayerNotesCommandHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BoardNotesResultDto> Handle(SaveBoardPlayerNotesCommand request, CancellationToken cancellationToken)
    {
        var entry = await _dbContext.ScoutBoardEntries
            .FirstOrDefaultAsync(e => e.BoardId == request.BoardId && e.PlayerId == request.PlayerId, cancellationToken);

        if (entry == null)
        {
            throw new KeyNotFoundException($"Board entry for board '{request.BoardId}' and player '{request.PlayerId}' was not found.");
        }

        entry.SetNotes(request.Notes);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new BoardNotesResultDto(entry.Notes);
    }
}
