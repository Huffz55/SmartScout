using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;

namespace SmartScout.Application.UseCases.Analytics;

public class GetSleeperPlayersQueryHandler : IRequestHandler<GetSleeperPlayersQuery, List<SleeperPlayerDto>>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly ISleeperScoutingService _sleeperService;

    public GetSleeperPlayersQueryHandler(
        ISmartScoutDbContext dbContext,
        ISleeperScoutingService sleeperService)
    {
        _dbContext = dbContext;
        _sleeperService = sleeperService;
    }

    public async Task<List<SleeperPlayerDto>> Handle(GetSleeperPlayersQuery request, CancellationToken cancellationToken)
    {
        var allStats = await _dbContext.SeasonStats
            .Include(s => s.Player)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Fetch full ranked pool before applying post-discovery Age/Position filters so U-22 or position-specific queries are never truncated at global Top-50
        bool hasPostFilter = request.MaxAge.HasValue || (!string.IsNullOrWhiteSpace(request.Position) && request.Position != "ALL");
        int fetchCount = hasPostFilter
            ? Math.Max(request.Top * 20, 2000)
            : request.Top;

        var sleepers = _sleeperService.DiscoverSleepers(
            allStats,
            request.Category,
            request.League,
            request.Season,
            fetchCount);

        // Strict defensive age filter: exclude records where Age is missing or exceeds MaxAge (e.g. U-22 guarantees Age <= 22)
        if (request.MaxAge.HasValue)
        {
            sleepers = sleepers.Where(s => s.Age.HasValue && s.Age.Value <= request.MaxAge.Value).ToList();
        }

        // Defensive position filter
        if (!string.IsNullOrWhiteSpace(request.Position) && request.Position != "ALL")
        {
            var posKey = request.Position.Trim().ToUpperInvariant();
            sleepers = sleepers.Where(s => !string.IsNullOrWhiteSpace(s.Position) && s.Position.ToUpperInvariant().Contains(posKey)).ToList();
        }

        return sleepers.Take(request.Top).ToList();
    }
}
