using MediatR;
using Microsoft.Extensions.Caching.Memory;
using SmartScout.Application.Common.Queries;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.Analytics;

public class SmartScoutSearchQueryHandler : IRequestHandler<SmartScoutSearchQuery, List<SmartScoutSearchResultDto>>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly ISmartScoutSearchEngine _searchEngine;
    private readonly IMemoryCache _cache;
    private readonly IScoutingCacheState? _cacheState;

    public SmartScoutSearchQueryHandler(
        ISmartScoutDbContext dbContext,
        ISmartScoutSearchEngine searchEngine,
        IMemoryCache cache,
        IScoutingCacheState? cacheState = null)
    {
        _dbContext = dbContext;
        _searchEngine = searchEngine;
        _cache = cache;
        _cacheState = cacheState;
    }

    public async Task<List<SmartScoutSearchResultDto>> Handle(SmartScoutSearchQuery request, CancellationToken cancellationToken)
    {
        long version = _cacheState?.Version ?? 0;
        string cacheKey = $"smart_search_v{version}|{request.Prompt.Trim().ToLowerInvariant()}|{request.Season}|{request.Top}";
        if (_cache.TryGetValue(cacheKey, out List<SmartScoutSearchResultDto>? cached) && cached != null)
        {
            return cached;
        }

        string cleanPrompt = request.Prompt.ToLowerInvariant();
        bool wantsBuyLowHistory = cleanPrompt.Contains("uyuyan") || cleanPrompt.Contains("buy low") || cleanPrompt.Contains("buy-low") ||
                                  cleanPrompt.Contains("bounce back") || cleanPrompt.Contains("bounce-back") || cleanPrompt.Contains("parlaya") ||
                                  cleanPrompt.Contains("patlama") || cleanPrompt.Contains("sleeper") || cleanPrompt.Contains("undervalued") ||
                                  cleanPrompt.Contains("firsat") || cleanPrompt.Contains("fırsat") || cleanPrompt.Contains("potansiyel") ||
                                  cleanPrompt.Contains("eskiden") || cleanPrompt.Contains("gecmiste") || cleanPrompt.Contains("geçmişte");

        Expression<Func<PlayerSeasonStat, bool>> predicate;
        if (request.Season.HasValue)
        {
            int targetYear = request.Season.Value;
            int minYear = targetYear - 3;
            predicate = wantsBuyLowHistory
                ? s => s.GamesPlayed >= 3 && s.Season <= targetYear && s.Season >= minYear
                : s => s.GamesPlayed >= 3 && s.Season == targetYear;
        }
        else
        {
            predicate = s => s.GamesPlayed >= 3;
        }

        var candidates = await SeasonStatAnalyticsLoader.LoadProjectedStatsAsync(
            _dbContext,
            predicate,
            linkCareerHistory: true,
            cancellationToken);

        var results = _searchEngine.Search(request.Prompt, candidates, request.Top, request.Season);

        _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));

        return results;
    }
}
