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

public class GetSimilarPlayersQueryHandler : IRequestHandler<GetSimilarPlayersQuery, List<SimilarPlayerDto>>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IPlayerAnalyticsService _analyticsService;
    private readonly IMemoryCache _cache;

    public GetSimilarPlayersQueryHandler(
        ISmartScoutDbContext dbContext,
        IPlayerAnalyticsService analyticsService,
        IMemoryCache cache)
    {
        _dbContext = dbContext;
        _analyticsService = analyticsService;
        _cache = cache;
    }

    public async Task<List<SimilarPlayerDto>> Handle(GetSimilarPlayersQuery request, CancellationToken cancellationToken)
    {
        string cacheKey = $"sim_{request.PlayerId}_{request.Season}_{request.Top}_{request.MinGamesPlayed}_{request.ExcludeSamePlayer}";
        if (_cache.TryGetValue(cacheKey, out List<SimilarPlayerDto>? cached) && cached != null)
        {
            return cached;
        }

        var targetStat = await _dbContext.SeasonStats
            .Include(s => s.Player)
            .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerId && s.Season == request.Season, cancellationToken);

        if (targetStat == null || targetStat.Player == null)
        {
            throw new KeyNotFoundException($"Season {request.Season} stats for player ID '{request.PlayerId}' were not found.");
        }

        var query = _dbContext.SeasonStats
            .AsNoTracking()
            .Include(s => s.Player)
            .Where(s => s.GamesPlayed >= request.MinGamesPlayed);

        if (request.ExcludeSamePlayer)
        {
            query = query.Where(s => s.PlayerId != request.PlayerId);
        }
        else
        {
            query = query.Where(s => s.Id != targetStat.Id);
        }

        var candidates = await query.ToListAsync(cancellationToken);

        var results = candidates
            .Where(c => c.Player != null)
            .Select(c => new
            {
                Stat = c,
                Player = c.Player!,
                SimilarityScore = _analyticsService.CalculateSimilarity(targetStat, c)
            })
            .OrderByDescending(x => x.SimilarityScore)
            .Take(request.Top)
            .Select(x => new SimilarPlayerDto
            {
                PlayerId = x.Player.Id,
                FirstName = x.Player.FirstName,
                LastName = x.Player.LastName,
                TeamName = x.Player.TeamName,
                Position = x.Player.Position,
                Season = x.Stat.Season,
                SimilarityScore = x.SimilarityScore,
                Archetype = _analyticsService.DetermineArchetype(x.Stat, x.Player.Position, x.Player.Height).ToString(),
                GamesPlayed = x.Stat.GamesPlayed,
                Minutes = x.Stat.Minutes,
                Points = x.Stat.Points,
                Assists = x.Stat.Assists,
                Rebounds = x.Stat.Rebounds,
                Steals = x.Stat.Steals,
                Blocks = x.Stat.Blocks,
                TrueShootingPct = x.Stat.TrueShootingPct,
                PlayerEfficiencyRating = x.Stat.PlayerEfficiencyRating,
                UsagePct = x.Stat.UsagePct,
                WinShares = x.Stat.WinShares,
                BoxPlusMinus = x.Stat.BoxPlusMinus,
                Pir = x.Stat.Pir,
                FoulsDrawn = x.Stat.FoulsDrawn,
                PointsPer40 = x.Stat.PointsPer40,
                Nationality = x.Player.Nationality,
                League = x.Player.League,
                RadarMetrics = _analyticsService.CalculateRadarMetrics(x.Stat)
            })
            .ToList();

        _cache.Set(cacheKey, results, TimeSpan.FromMinutes(30));

        return results;
    }
}
