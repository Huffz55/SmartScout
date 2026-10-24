using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.Analytics;

public class ComparePlayersQueryHandler : IRequestHandler<ComparePlayersQuery, PlayerComparisonDto>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IPlayerAnalyticsService _analyticsService;
    private readonly IMemoryCache _cache;

    public ComparePlayersQueryHandler(
        ISmartScoutDbContext dbContext,
        IPlayerAnalyticsService analyticsService,
        IMemoryCache cache)
    {
        _dbContext = dbContext;
        _analyticsService = analyticsService;
        _cache = cache;
    }

    public async Task<PlayerComparisonDto> Handle(ComparePlayersQuery request, CancellationToken cancellationToken)
    {
        string cacheKey = $"comp_{request.PlayerAId}_{request.SeasonA}_{request.PlayerBId}_{request.SeasonB}";
        if (_cache.TryGetValue(cacheKey, out PlayerComparisonDto? cached) && cached != null)
        {
            return cached;
        }

        var statA = await _dbContext.SeasonStats
            .Include(s => s.Player)
            .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerAId && s.Season == request.SeasonA, cancellationToken);

        if (statA == null || statA.Player == null)
        {
            throw new KeyNotFoundException($"Season {request.SeasonA} stats for Player A ({request.PlayerAId}) were not found.");
        }

        var statB = await _dbContext.SeasonStats
            .Include(s => s.Player)
            .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerBId && s.Season == request.SeasonB, cancellationToken);

        if (statB == null || statB.Player == null)
        {
            throw new KeyNotFoundException($"Season {request.SeasonB} stats for Player B ({request.PlayerBId}) were not found.");
        }

        var result = _analyticsService.Compare(statA, statA.Player, statB, statB.Player);

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));

        return result;
    }
}
