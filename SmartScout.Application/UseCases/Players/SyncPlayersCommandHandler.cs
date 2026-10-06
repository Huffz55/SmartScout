using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.Players;

public class SyncPlayersCommandHandler : IRequestHandler<SyncPlayersCommand, int>
{
    private readonly IEnumerable<IPlayerSyncStrategy> _syncStrategies;
    private readonly ISmartScoutDbContext _dbContext;

    public SyncPlayersCommandHandler(IEnumerable<IPlayerSyncStrategy> syncStrategies, ISmartScoutDbContext dbContext)
    {
        _syncStrategies = syncStrategies;
        _dbContext = dbContext;
    }

    public async Task<int> Handle(SyncPlayersCommand request, CancellationToken cancellationToken)
    {
        int totalUpsertedCount = 0;

        // Iterate through all injected strategies
        foreach (var strategy in _syncStrategies)
        {
            await foreach (var playersBatch in strategy.FetchPlayersAsync(request.Cursor, cancellationToken))
            {
                var batchList = playersBatch.ToList();
                if (!batchList.Any()) continue;

                var apiIds = batchList.Select(p => p.ExternalApiId).ToList();

                var existingPlayers = await _dbContext.Players
                    .Where(p => apiIds.Contains(p.ExternalApiId))
                    .ToDictionaryAsync(p => p.ExternalApiId, cancellationToken);

                foreach (var newPlayer in batchList)
                {
                    if (existingPlayers.TryGetValue(newPlayer.ExternalApiId, out var existingPlayer))
                    {
                        existingPlayer.UpdateDetails(
                            newPlayer.FirstName,
                            newPlayer.LastName,
                            newPlayer.Position,
                            newPlayer.Height,
                            newPlayer.WeightPounds,
                            newPlayer.TeamName,
                            newPlayer.League
                        );
                    }
                    else
                    {
                        await _dbContext.Players.AddAsync(newPlayer, cancellationToken);
                    }
                    
                    totalUpsertedCount++;
                }

                // Crucially save changes immediately for each chunk
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return totalUpsertedCount;
    }
}
