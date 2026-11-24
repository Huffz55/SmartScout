using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartScout.Application.Common.Queries;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.UseCases.Analytics;

public class GetSleeperPlayersQueryHandler : IRequestHandler<GetSleeperPlayersQuery, List<SleeperPlayerDto>>
{
    private static readonly SemaphoreSlim DatasetLoadLock = new(1, 1);

    private readonly ISmartScoutDbContext _dbContext;
    private readonly ISleeperScoutingService _sleeperService;
    private readonly IMemoryCache? _cache;
    private readonly IScoutingCacheState? _cacheState;

    public GetSleeperPlayersQueryHandler(
        ISmartScoutDbContext dbContext,
        ISleeperScoutingService sleeperService,
        IMemoryCache? cache = null,
        IScoutingCacheState? cacheState = null)
    {
        _dbContext = dbContext;
        _sleeperService = sleeperService;
        _cache = cache;
        _cacheState = cacheState;
    }

    public async Task<List<SleeperPlayerDto>> Handle(GetSleeperPlayersQuery request, CancellationToken cancellationToken)
    {
        long version = _cacheState?.Version ?? 0;

        // Fetch full ranked pool before applying post-discovery Age/Position filters so U-24 or position-specific queries are never truncated at global Top-50
        bool hasPositionFilter = !string.IsNullOrWhiteSpace(request.Position)
            && !string.Equals(request.Position.Trim(), "ALL", StringComparison.OrdinalIgnoreCase);
        bool hasPostFilter = request.MaxAge.HasValue || hasPositionFilter;
        int fetchCount = hasPostFilter
            ? Math.Max(request.Top * 20, 2000)
            : request.Top;

        string rankedKey = IScoutingCacheState.BuildVersionedKey(version, "sleeper_ranked", request.Category ?? "", request.League ?? "", request.Season?.ToString() ?? "", fetchCount);
        List<SleeperPlayerDto>? sleepers = null;

        if (_cache != null && _cache.TryGetValue(rankedKey, out List<SleeperPlayerDto>? cachedRanked) && cachedRanked != null)
        {
            sleepers = cachedRanked;
        }
        else
        {
            var allStats = await LoadAllStatsCachedAsync(version, cancellationToken);
            sleepers = _sleeperService.DiscoverSleepers(
                allStats,
                request.Category,
                request.League,
                request.Season,
                fetchCount);

            _cache?.Set(rankedKey, sleepers, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            });
        }

        IEnumerable<SleeperPlayerDto> filtered = sleepers;

        // Strict defensive age filter: exclude records where Age is missing or exceeds MaxAge (e.g. U-24 guarantees Age <= 24)
        if (request.MaxAge.HasValue)
        {
            filtered = filtered.Where(s => s.Age.HasValue && s.Age.Value <= request.MaxAge.Value);
        }

        // Defensive position filter (checks both primary and secondary positions for combo players like G-F or PF/C)
        if (hasPositionFilter)
        {
            var posKey = request.Position!.Trim().ToUpperInvariant();
            filtered = filtered.Where(s =>
                (!string.IsNullOrWhiteSpace(s.Position) && s.Position.ToUpperInvariant().Contains(posKey)) ||
                (!string.IsNullOrWhiteSpace(s.SecondaryPosition) && s.SecondaryPosition.ToUpperInvariant().Contains(posKey)));
        }

        // Clone returned DTOs before overlaying live active injuries so shared cache entries stay immutable
        var finalSleepers = filtered.Take(request.Top).Select(s => s.Clone()).ToList();
        if (finalSleepers.Count > 0)
        {
            var targetIds = finalSleepers.Select(s => s.PlayerId).Distinct().ToList();
            var activeInjuries = await _dbContext.PlayerInjuries
                .AsNoTracking()
                .Where(i => targetIds.Contains(i.PlayerId) &&
                            i.Status != "Recovered" &&
                            i.Status != "Healthy" &&
                            i.Status != "Cleared")
                .OrderByDescending(i => i.ReportedAt)
                .ToListAsync(cancellationToken);

            var injuryByPlayer = activeInjuries
                .GroupBy(i => i.PlayerId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var sleeper in finalSleepers)
            {
                if (injuryByPlayer.TryGetValue(sleeper.PlayerId, out var inj))
                {
                    sleeper.ActiveInjuryStatus = inj.Status;
                    sleeper.ActiveInjuryDetail = inj.InjuryType;
                }
            }
        }

        return finalSleepers;
    }

    private async Task<List<PlayerSeasonStat>> LoadAllStatsCachedAsync(long version, CancellationToken cancellationToken)
    {
        Task<List<PlayerSeasonStat>> FetchProjectedAsync() =>
            SeasonStatAnalyticsLoader.LoadProjectedStatsAsync(_dbContext, predicate: null, linkCareerHistory: false, cancellationToken);

        if (_cache == null)
        {
            return await FetchProjectedAsync();
        }

        string datasetKey = IScoutingCacheState.BuildVersionedKey(version, "sleeper_dataset");
        if (_cache.TryGetValue(datasetKey, out List<PlayerSeasonStat>? cached) && cached != null)
        {
            return cached;
        }

        await DatasetLoadLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(datasetKey, out cached) && cached != null)
            {
                return cached;
            }

            var loaded = await FetchProjectedAsync();

            _cache.Set(datasetKey, loaded, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            });

            return loaded;
        }
        finally
        {
            DatasetLoadLock.Release();
        }
    }
}
