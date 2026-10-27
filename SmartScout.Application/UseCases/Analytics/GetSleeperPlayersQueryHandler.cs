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

        return _sleeperService.DiscoverSleepers(
            allStats,
            request.Category,
            request.League,
            request.Season,
            request.Top);
    }
}
