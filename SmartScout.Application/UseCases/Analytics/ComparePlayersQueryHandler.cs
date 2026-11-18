using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.Analytics;

public class ComparePlayersQueryHandler : IRequestHandler<ComparePlayersQuery, PlayerComparisonDto>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IPlayerAnalyticsService _analyticsService;
    private readonly IMemoryCache _cache;
    private readonly IScoutingCacheState? _cacheState;

    public ComparePlayersQueryHandler(
        ISmartScoutDbContext dbContext,
        IPlayerAnalyticsService analyticsService,
        IMemoryCache cache,
        IScoutingCacheState? cacheState = null)
    {
        _dbContext = dbContext;
        _analyticsService = analyticsService;
        _cache = cache;
        _cacheState = cacheState;
    }

    public async Task<PlayerComparisonDto> Handle(ComparePlayersQuery request, CancellationToken cancellationToken)
    {
        string cacheKey = IScoutingCacheState.BuildVersionedKey(
            _cacheState?.Version ?? 0,
            "comp",
            request.PlayerAId,
            request.SeasonA,
            request.PlayerBId,
            request.SeasonB);
        if (_cache.TryGetValue(cacheKey, out PlayerComparisonDto? cached) && cached != null)
        {
            return cached;
        }

        var statA = await _dbContext.SeasonStats
            .Include(s => s.Player)
                .ThenInclude(p => p!.Injuries)
            .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerAId && s.Season == request.SeasonA, cancellationToken)
            ?? await _dbContext.SeasonStats
                .Include(s => s.Player)
                    .ThenInclude(p => p!.Injuries)
                .OrderByDescending(s => s.Season)
                .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerAId, cancellationToken);

        if (statA == null || statA.Player == null)
        {
            throw new KeyNotFoundException($"Season {request.SeasonA} stats for Player A ({request.PlayerAId}) were not found.");
        }

        var statB = await _dbContext.SeasonStats
            .Include(s => s.Player)
                .ThenInclude(p => p!.Injuries)
            .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerBId && s.Season == request.SeasonB, cancellationToken)
            ?? await _dbContext.SeasonStats
                .Include(s => s.Player)
                    .ThenInclude(p => p!.Injuries)
                .OrderByDescending(s => s.Season)
                .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerBId, cancellationToken);

        if (statB == null || statB.Player == null)
        {
            throw new KeyNotFoundException($"Season {request.SeasonB} stats for Player B ({request.PlayerBId}) were not found.");
        }

        var result = _analyticsService.Compare(statA, statA.Player, statB, statB.Player);

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));

        return result;
    }
}
