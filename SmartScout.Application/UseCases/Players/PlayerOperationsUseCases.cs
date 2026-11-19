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

namespace SmartScout.Application.UseCases.Players;

public sealed record SyncStatusDto(
    bool EnableBackgroundSync,
    bool EnableLiveInjuryAutoSync,
    int InjurySyncIntervalHours,
    int CurrentSeasonStatRefreshHours,
    int LiveSeasonYear,
    DateTime? LastInjurySyncAt,
    DateTime? LastSeasonStatSyncAt,
    int ActiveInjuriesCount,
    int TotalPlayersCount,
    int LiveSeasonStatRowsCount);

public sealed record WatchlistLatestStatDto(
    int Season,
    decimal? Points,
    decimal? Rebounds,
    decimal? Assists,
    decimal? Minutes,
    decimal? TrueShootingPct,
    decimal? EuroLeagueTransitionIndex,
    decimal? StocksPer36);

public sealed record WatchlistPlayerDto(
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
    WatchlistLatestStatDto? LatestStat);

public sealed record SeededWatchlistPlayerDto(
    Guid Id,
    string FullName,
    string League,
    string? PrimaryPosition,
    string? Nationality);

public sealed record SeedDemoWatchlistResultDto(
    int AddedCount,
    int TotalWatchlisted,
    IReadOnlyList<SeededWatchlistPlayerDto> Players);

public sealed record ToggleWatchlistResultDto(bool IsWatchlisted);
public sealed record SaveNotesResultDto(string? ScoutNotes);

public record GetSyncStatusQuery : IRequest<SyncStatusDto>;

public class GetSyncStatusQueryHandler : IRequestHandler<GetSyncStatusQuery, SyncStatusDto>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IScraperRuntimeSettings _settings;

    public GetSyncStatusQueryHandler(ISmartScoutDbContext dbContext, IScraperRuntimeSettings settings)
    {
        _dbContext = dbContext;
        _settings = settings;
    }

    public async Task<SyncStatusDto> Handle(GetSyncStatusQuery request, CancellationToken cancellationToken)
    {
        var lastInjuryCheckpoint = await _dbContext.IngestionJobLogs
            .AsNoTracking()
            .Where(l => l.LeagueCode == SyncCheckpointConstants.InjuryCheckpointCode && l.Status == "Completed")
            .OrderByDescending(l => l.UpdatedAt)
            .Select(l => (DateTime?)l.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var lastInjuryReportedAt = await _dbContext.PlayerInjuries
            .AsNoTracking()
            .MaxAsync(i => (DateTime?)i.ReportedAt, cancellationToken);

        var lastSeasonStatSyncAt = await _dbContext.IngestionJobLogs
            .AsNoTracking()
            .Where(l => l.Season == _settings.LiveSeasonYear
                && l.LeagueCode != SyncCheckpointConstants.InjuryCheckpointCode
                && l.Status == "Completed")
            .MaxAsync(l => (DateTime?)l.UpdatedAt, cancellationToken);

        int activeInjuriesCount = await _dbContext.PlayerInjuries
            .AsNoTracking()
            .CountAsync(InjuryPredicates.IsActive, cancellationToken);

        int totalPlayersCount = await _dbContext.Players
            .AsNoTracking()
            .CountAsync(cancellationToken);

        int liveSeasonStatRowsCount = await _dbContext.SeasonStats
            .AsNoTracking()
            .CountAsync(s => s.Season == _settings.LiveSeasonYear, cancellationToken);

        return new SyncStatusDto(
            _settings.EnableBackgroundSync,
            _settings.EnableLiveInjuryAutoSync,
            _settings.InjurySyncIntervalHours,
            _settings.CurrentSeasonStatRefreshHours,
            _settings.LiveSeasonYear,
            lastInjuryCheckpoint ?? lastInjuryReportedAt,
            lastSeasonStatSyncAt,
            activeInjuriesCount,
            totalPlayersCount,
            liveSeasonStatRowsCount);
    }
}

public record EnrichPlayerBiosCommand(string? League = null, int Limit = 50) : IRequest<BioEnrichmentSummaryDto>;

public class EnrichPlayerBiosCommandValidator : AbstractValidator<EnrichPlayerBiosCommand>
{
    public EnrichPlayerBiosCommandValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 2000);
    }
}

public class EnrichPlayerBiosCommandHandler : IRequestHandler<EnrichPlayerBiosCommand, BioEnrichmentSummaryDto>
{
    private readonly IPlayerBioEnrichmentService _enrichmentService;

    public EnrichPlayerBiosCommandHandler(IPlayerBioEnrichmentService enrichmentService)
    {
        _enrichmentService = enrichmentService;
    }

    public Task<BioEnrichmentSummaryDto> Handle(EnrichPlayerBiosCommand request, CancellationToken cancellationToken) =>
        _enrichmentService.EnrichPlayerBiosAsync(request.League, request.Limit, cancellationToken);
}

public record ToggleWatchlistCommand(Guid PlayerId) : IRequest<ToggleWatchlistResultDto>;

public class ToggleWatchlistCommandValidator : AbstractValidator<ToggleWatchlistCommand>
{
    public ToggleWatchlistCommandValidator()
    {
        RuleFor(x => x.PlayerId).NotEmpty();
    }
}

public class ToggleWatchlistCommandHandler : IRequestHandler<ToggleWatchlistCommand, ToggleWatchlistResultDto>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IScoutingCacheState? _cacheState;

    public ToggleWatchlistCommandHandler(ISmartScoutDbContext dbContext, IScoutingCacheState? cacheState = null)
    {
        _dbContext = dbContext;
        _cacheState = cacheState;
    }

    public async Task<ToggleWatchlistResultDto> Handle(ToggleWatchlistCommand request, CancellationToken cancellationToken)
    {
        var player = await _dbContext.Players.FirstOrDefaultAsync(p => p.Id == request.PlayerId, cancellationToken);
        if (player == null)
        {
            throw new KeyNotFoundException($"Player '{request.PlayerId}' was not found.");
        }

        player.ToggleWatchlist();
        await _dbContext.SaveChangesAsync(cancellationToken);
        _cacheState?.BumpVersion();
        return new ToggleWatchlistResultDto(player.IsWatchlisted);
    }
}

public record SavePlayerNotesCommand(Guid PlayerId, string? Notes) : IRequest<SaveNotesResultDto>;

public class SavePlayerNotesCommandValidator : AbstractValidator<SavePlayerNotesCommand>
{
    public SavePlayerNotesCommandValidator()
    {
        RuleFor(x => x.PlayerId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000).WithMessage("Notes cannot exceed 2000 characters.");
    }
}

public class SavePlayerNotesCommandHandler : IRequestHandler<SavePlayerNotesCommand, SaveNotesResultDto>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IScoutingCacheState? _cacheState;

    public SavePlayerNotesCommandHandler(ISmartScoutDbContext dbContext, IScoutingCacheState? cacheState = null)
    {
        _dbContext = dbContext;
        _cacheState = cacheState;
    }

    public async Task<SaveNotesResultDto> Handle(SavePlayerNotesCommand request, CancellationToken cancellationToken)
    {
        var player = await _dbContext.Players.FirstOrDefaultAsync(p => p.Id == request.PlayerId, cancellationToken);
        if (player == null)
        {
            throw new KeyNotFoundException($"Player '{request.PlayerId}' was not found.");
        }

        player.SetScoutNotes(request.Notes);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _cacheState?.BumpVersion();
        return new SaveNotesResultDto(player.ScoutNotes);
    }
}

public record GetWatchlistQuery : IRequest<List<WatchlistPlayerDto>>;

public class GetWatchlistQueryHandler : IRequestHandler<GetWatchlistQuery, List<WatchlistPlayerDto>>
{
    private readonly ISmartScoutDbContext _dbContext;

    public GetWatchlistQueryHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<WatchlistPlayerDto>> Handle(GetWatchlistQuery request, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Players
            .AsNoTracking()
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
                p.BirthDate,
                p.ScoutNotes,
                p.EstimatedMarketValueTier,
                p.ContractExpirySeason,
                LatestStat = p.SeasonStats
                    .OrderByDescending(s => s.Season)
                    .Select(s => new WatchlistLatestStatDto(
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

        return rows.Select(p => new WatchlistPlayerDto(
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
            Player.CalculateAgeFromBirthDate(p.BirthDate),
            p.ScoutNotes,
            p.EstimatedMarketValueTier,
            p.ContractExpirySeason,
            p.LatestStat)).ToList();
    }
}

public record SeedDemoWatchlistCommand : IRequest<SeedDemoWatchlistResultDto>;

public class SeedDemoWatchlistCommandHandler : IRequestHandler<SeedDemoWatchlistCommand, SeedDemoWatchlistResultDto>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IScraperRuntimeSettings _settings;
    private readonly IScoutingCacheState? _cacheState;

    public SeedDemoWatchlistCommandHandler(
        ISmartScoutDbContext dbContext,
        IScraperRuntimeSettings settings,
        IScoutingCacheState? cacheState = null)
    {
        _dbContext = dbContext;
        _settings = settings;
        _cacheState = cacheState;
    }

    public async Task<SeedDemoWatchlistResultDto> Handle(SeedDemoWatchlistCommand request, CancellationToken cancellationToken)
    {
        int existingCount = await _dbContext.Players.CountAsync(p => p.IsWatchlisted, cancellationToken);
        if (existingCount > 0)
        {
            return new SeedDemoWatchlistResultDto(0, existingCount, Array.Empty<SeededWatchlistPlayerDto>());
        }

        int minSeason = _settings.LiveSeasonYear - 1;
        var candidates = await _dbContext.Players
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
            await _dbContext.SaveChangesAsync(cancellationToken);
            _cacheState?.BumpVersion();
        }

        return new SeedDemoWatchlistResultDto(
            selected.Count,
            selected.Count,
            selected.Select(p => new SeededWatchlistPlayerDto(
                p.Id,
                p.FullName,
                p.League,
                p.PrimaryPosition,
                p.Nationality)).ToList());
    }

    public static List<Player> SelectDiverseDemoTargets(IEnumerable<Player> candidates, int maxCount = 6)
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
