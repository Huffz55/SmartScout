using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Common.Queries;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.UseCases.Injuries;

public sealed record InjurySummaryItemDto(
    Guid Id,
    Guid PlayerId,
    string PlayerName,
    string League,
    string? TeamName,
    int Season,
    string InjuryType,
    string? Status,
    int? GamesMissed,
    string? Source,
    string? Notes,
    DateTime? ReportedAt);

public sealed record InjuriesSummaryDto(
    int TotalRecords,
    int ActiveRecords,
    List<InjurySummaryItemDto> Items);

public sealed record SyncLiveInjuriesResponseDto(
    string Message,
    int TotalFetched,
    int MatchedPlayers,
    int InsertedCount,
    int UpdatedCount,
    int RecoveredMarkedCount,
    IReadOnlyDictionary<string, int> SourceCounts,
    IReadOnlyList<SyncedInjuryEntryDto> SampleMatched);

public record GetInjuriesSummaryQuery(bool ActiveOnly = true, int Top = 100) : IRequest<InjuriesSummaryDto>;

public class GetInjuriesSummaryQueryValidator : AbstractValidator<GetInjuriesSummaryQuery>
{
    public GetInjuriesSummaryQueryValidator()
    {
        RuleFor(x => x.Top).InclusiveBetween(1, 500);
    }
}

public class GetInjuriesSummaryQueryHandler : IRequestHandler<GetInjuriesSummaryQuery, InjuriesSummaryDto>
{
    private readonly ISmartScoutDbContext _dbContext;

    public GetInjuriesSummaryQueryHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InjuriesSummaryDto> Handle(GetInjuriesSummaryQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.PlayerInjuries.AsNoTracking();

        if (request.ActiveOnly)
        {
            query = query.Where(InjuryPredicates.IsActive);
        }

        var items = await query
            .OrderByDescending(i => i.ReportedAt)
            .Take(Math.Clamp(request.Top, 1, 500))
            .Select(i => new InjurySummaryItemDto(
                i.Id,
                i.PlayerId,
                i.Player != null ? i.Player.FullName : "Unknown",
                i.Player != null ? i.Player.League : "-",
                i.Player != null ? i.Player.TeamName : null,
                i.Season,
                i.InjuryType,
                i.Status,
                i.GamesMissed,
                i.Source,
                i.Notes,
                i.ReportedAt))
            .ToListAsync(cancellationToken);

        int totalCount = await _dbContext.PlayerInjuries.AsNoTracking().CountAsync(cancellationToken);
        int activeCount = await _dbContext.PlayerInjuries.AsNoTracking().CountAsync(
            InjuryPredicates.IsActive,
            cancellationToken);

        return new InjuriesSummaryDto(totalCount, activeCount, items);
    }
}

public record SyncLiveInjuriesCommand : IRequest<SyncLiveInjuriesResponseDto>;

public class SyncLiveInjuriesCommandHandler : IRequestHandler<SyncLiveInjuriesCommand, SyncLiveInjuriesResponseDto>
{
    private readonly IPlayerInjurySyncService _injurySyncService;
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IScraperRuntimeSettings _settings;

    public SyncLiveInjuriesCommandHandler(
        IPlayerInjurySyncService injurySyncService,
        ISmartScoutDbContext dbContext,
        IScraperRuntimeSettings settings)
    {
        _injurySyncService = injurySyncService;
        _dbContext = dbContext;
        _settings = settings;
    }

    public async Task<SyncLiveInjuriesResponseDto> Handle(SyncLiveInjuriesCommand request, CancellationToken cancellationToken)
    {
        var result = await _injurySyncService.SyncLiveInjuriesAsync(cancellationToken);

        int liveSeason = _settings.LiveSeasonYear;
        var injuryLog = await _dbContext.IngestionJobLogs
            .FirstOrDefaultAsync(l => l.LeagueCode == SyncCheckpointConstants.InjuryCheckpointCode && l.Season == liveSeason, cancellationToken);

        if (injuryLog == null)
        {
            injuryLog = new IngestionJobLog(SyncCheckpointConstants.InjuryCheckpointCode, liveSeason, "Completed", result.MatchedPlayers);
            await _dbContext.IngestionJobLogs.AddAsync(injuryLog, cancellationToken);
        }
        else
        {
            injuryLog.MarkCompleted(result.MatchedPlayers);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SyncLiveInjuriesResponseDto(
            $"Live injury sync completed: {result.MatchedPlayers} matched players ({result.InsertedCount} inserted, {result.UpdatedCount} updated, {result.RecoveredMarkedCount} recovered).",
            result.TotalFetched,
            result.MatchedPlayers,
            result.InsertedCount,
            result.UpdatedCount,
            result.RecoveredMarkedCount,
            result.SourceCounts,
            result.SampleMatched);
    }
}
