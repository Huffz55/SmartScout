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

public class SmartScoutSearchQueryHandler : IRequestHandler<SmartScoutSearchQuery, List<SmartScoutSearchResultDto>>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly ISmartScoutSearchEngine _searchEngine;
    private readonly IMemoryCache _cache;

    public SmartScoutSearchQueryHandler(
        ISmartScoutDbContext dbContext,
        ISmartScoutSearchEngine searchEngine,
        IMemoryCache cache)
    {
        _dbContext = dbContext;
        _searchEngine = searchEngine;
        _cache = cache;
    }

    public async Task<List<SmartScoutSearchResultDto>> Handle(SmartScoutSearchQuery request, CancellationToken cancellationToken)
    {
        string cacheKey = $"smart_search_{request.Prompt.Trim().ToLowerInvariant()}_{request.Season}_{request.Top}";
        if (_cache.TryGetValue(cacheKey, out List<SmartScoutSearchResultDto>? cached) && cached != null)
        {
            return cached;
        }

        var query = _dbContext.SeasonStats
            .AsNoTracking()
            .Include(s => s.Player)
            .Where(s => s.GamesPlayed >= 3);

        string cleanPrompt = request.Prompt.ToLowerInvariant();
        bool wantsBuyLowHistory = cleanPrompt.Contains("uyuyan") || cleanPrompt.Contains("buy low") || cleanPrompt.Contains("buy-low") ||
                                  cleanPrompt.Contains("bounce back") || cleanPrompt.Contains("bounce-back") || cleanPrompt.Contains("parlaya") ||
                                  cleanPrompt.Contains("patlama") || cleanPrompt.Contains("sleeper") || cleanPrompt.Contains("undervalued") ||
                                  cleanPrompt.Contains("firsat") || cleanPrompt.Contains("fırsat") || cleanPrompt.Contains("potansiyel") ||
                                  cleanPrompt.Contains("eskiden") || cleanPrompt.Contains("gecmiste") || cleanPrompt.Contains("geçmişte");

        if (request.Season.HasValue)
        {
            if (wantsBuyLowHistory)
            {
                // Include recent prior seasons so Buy-Low / Bounce-Back index can compare current form vs prior peak
                int targetYear = request.Season.Value;
                query = query.Where(s => s.Season <= targetYear && s.Season >= targetYear - 3);
            }
            else
            {
                query = query.Where(s => s.Season == request.Season.Value);
            }
        }

        var candidates = await query.ToListAsync(cancellationToken);

        var results = _searchEngine.Search(request.Prompt, candidates, request.Top, request.Season);

        _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));

        return results;
    }
}
