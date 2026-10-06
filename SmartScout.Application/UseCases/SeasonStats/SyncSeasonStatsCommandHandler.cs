using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.SeasonStats;

public class SyncSeasonStatsCommandHandler : IRequestHandler<SyncSeasonStatsCommand, int>
{
    private readonly IEnumerable<ISeasonStatSyncStrategy> _syncStrategies;
    private readonly ISmartScoutDbContext _dbContext;

    public SyncSeasonStatsCommandHandler(IEnumerable<ISeasonStatSyncStrategy> syncStrategies, ISmartScoutDbContext dbContext)
    {
        _syncStrategies = syncStrategies;
        _dbContext = dbContext;
    }

    public async Task<int> Handle(SyncSeasonStatsCommand request, CancellationToken cancellationToken)
    {
        int totalUpsertedCount = 0;

        // Fetch players. To optimize the initial mass import, we prioritize players without stats. 
        // For a true ongoing delta update, this filter can be removed or adjusted.
        var playersToFetch = await _dbContext.Players
            .Where(p => !p.SeasonStats.Any())
            .ToListAsync(cancellationToken);

        if (!playersToFetch.Any()) return 0;

        foreach (var strategy in _syncStrategies)
        {
            await foreach (var statsBatch in strategy.FetchSeasonStatsAsync(playersToFetch, cancellationToken))
            {
                var batchList = statsBatch.ToList();
                if (!batchList.Any()) continue;

                var playerIdsInBatch = batchList
                    .Select(s => s.PlayerId)
                    .Distinct()
                    .ToList();

                var existingStatsList = await _dbContext.SeasonStats
                    .Where(s => playerIdsInBatch.Contains(s.PlayerId))
                    .ToListAsync(cancellationToken);

                var existingLookup = existingStatsList
                    .GroupBy(s => new { s.PlayerId, s.Season })
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var incomingStat in batchList)
                {
                    var key = new { incomingStat.PlayerId, incomingStat.Season };

                    if (existingLookup.TryGetValue(key, out var existingStat))
                    {
                        existingStat.UpdateStats(
                            incomingStat.GamesPlayed,
                            incomingStat.Minutes,
                            incomingStat.Points,
                            incomingStat.Assists,
                            incomingStat.Rebounds,
                            incomingStat.Steals,
                            incomingStat.Blocks,
                            incomingStat.FgPct,
                            incomingStat.Fg3Pct,
                            incomingStat.FtPct,
                            incomingStat.Turnovers,
                            incomingStat.TrueShootingPct,
                            incomingStat.PlayerEfficiencyRating,
                            incomingStat.UsagePct,
                            incomingStat.TotalReboundPct,
                            incomingStat.AssistPct,
                            incomingStat.WinShares,
                            incomingStat.BoxPlusMinus
                        );
                    }
                    else
                    {
                        await _dbContext.SeasonStats.AddAsync(incomingStat, cancellationToken);
                    }
                    
                    totalUpsertedCount++;
                }

                // Crucially save changes immediately per chunk
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return totalUpsertedCount;
    }
}
