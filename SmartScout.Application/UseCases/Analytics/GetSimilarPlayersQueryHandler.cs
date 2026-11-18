using MediatR;
using Microsoft.Extensions.Caching.Memory;
using SmartScout.Application.Common.Queries;
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
    private readonly IScoutingCacheState? _cacheState;

    public GetSimilarPlayersQueryHandler(
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

    public async Task<List<SimilarPlayerDto>> Handle(GetSimilarPlayersQuery request, CancellationToken cancellationToken)
    {
        string cacheKey = IScoutingCacheState.BuildVersionedKey(
            _cacheState?.Version ?? 0,
            "sim",
            request.PlayerId,
            request.Season,
            request.Top,
            request.MinGamesPlayed,
            request.ExcludeSamePlayer);

        if (_cache.TryGetValue(cacheKey, out List<SimilarPlayerDto>? cached) && cached != null)
        {
            return cached;
        }

        Guid playerId = request.PlayerId;
        int season = request.Season;
        var targetList = await SeasonStatAnalyticsLoader.LoadProjectedStatsAsync(
            _dbContext,
            s => s.PlayerId == playerId && s.Season == season,
            linkCareerHistory: false,
            cancellationToken);

        var targetStat = targetList
            .OrderByDescending(s => s.GamesPlayed)
            .ThenByDescending(s => s.LeagueDifficultyCoefficient)
            .FirstOrDefault();
        if (targetStat == null || targetStat.Player == null)
        {
            throw new KeyNotFoundException($"Season {request.Season} stats for player ID '{request.PlayerId}' were not found.");
        }

        int minGp = request.MinGamesPlayed;
        Guid targetStatId = targetStat.Id;
        bool excludeSame = request.ExcludeSamePlayer;
        var candidates = await SeasonStatAnalyticsLoader.LoadProjectedStatsAsync(
            _dbContext,
            s => s.GamesPlayed >= minGp && (excludeSame ? s.PlayerId != playerId : s.Id != targetStatId),
            linkCareerHistory: false,
            cancellationToken);

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
                TeamName = !string.IsNullOrWhiteSpace(x.Stat.TeamName) ? x.Stat.TeamName : x.Player.TeamName,
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
                League = !string.IsNullOrWhiteSpace(x.Stat.League) ? x.Stat.League : x.Player.League,
                RadarMetrics = _analyticsService.CalculateRadarMetrics(x.Stat)
            })
            .ToList();

        _cache.Set(cacheKey, results, TimeSpan.FromMinutes(30));

        return results;
    }
}
