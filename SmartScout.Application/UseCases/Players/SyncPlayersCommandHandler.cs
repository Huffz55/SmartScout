using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using System;
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
        // 1. Idempotently seed reference Leagues and Seasons if missing
        await EnsureReferenceDataSeededAsync(cancellationToken);

        int? parsedSeason = BasketballSeason.ParseSeasonEndYear(request.Season);
        var filterContext = new SyncFilterContext
        {
            LeagueCode = request.League,
            Season = parsedSeason,
            Force = request.Force
        };

        int totalUpsertedCount = 0;

        // 2. Iterate through all injected strategies with filter context
        foreach (var strategy in _syncStrategies)
        {
            await foreach (var playersBatch in strategy.FetchPlayersAsync(request.Cursor, cancellationToken, filterContext))
            {
                var batchList = playersBatch.ToList();
                if (!batchList.Any()) continue;

                var apiIds = batchList.Select(p => p.ExternalApiId).Distinct().ToList();
                var normalizedNames = batchList
                    .Select(p => p.NormalizedName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                var matchingPlayers = await _dbContext.Players
                    .Where(p => apiIds.Contains(p.ExternalApiId) || normalizedNames.Contains(p.NormalizedName))
                    .ToListAsync(cancellationToken);

                var existingById = matchingPlayers
                    .GroupBy(p => p.ExternalApiId)
                    .ToDictionary(g => g.Key, g => g.First());

                var existingByName = matchingPlayers
                    .Where(p => !string.IsNullOrWhiteSpace(p.NormalizedName))
                    .GroupBy(p => p.NormalizedName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var newPlayer in batchList)
                {
                    Player? existingPlayer = null;
                    if (existingById.TryGetValue(newPlayer.ExternalApiId, out var byId))
                    {
                        existingPlayer = byId;
                    }
                    else if (!string.IsNullOrWhiteSpace(newPlayer.NormalizedName) &&
                             existingByName.TryGetValue(newPlayer.NormalizedName, out var byName))
                    {
                        existingPlayer = byName;
                    }

                    if (existingPlayer != null)
                    {
                        existingPlayer.UpdateDetails(
                            newPlayer.FirstName,
                            newPlayer.LastName,
                            newPlayer.Position,
                            newPlayer.Height,
                            newPlayer.WeightPounds,
                            newPlayer.TeamName,
                            newPlayer.League,
                            newPlayer.Nationality,
                            newPlayer.BirthDate,
                            newPlayer.HeightCm,
                            newPlayer.WeightKg,
                            newPlayer.WingspanCm,
                            newPlayer.DraftYear,
                            newPlayer.DraftPick,
                            newPlayer.ExternalIdsJson
                        );
                    }
                    else
                    {
                        await _dbContext.Players.AddAsync(newPlayer, cancellationToken);
                        existingById[newPlayer.ExternalApiId] = newPlayer;
                        if (!string.IsNullOrWhiteSpace(newPlayer.NormalizedName))
                        {
                            existingByName[newPlayer.NormalizedName] = newPlayer;
                        }
                    }
                    
                    totalUpsertedCount++;
                }

                // Crucially save changes immediately for each chunk
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return totalUpsertedCount;
    }

    private async Task EnsureReferenceDataSeededAsync(CancellationToken cancellationToken)
    {
        var existingLeagueCodes = await _dbContext.Leagues
            .Select(l => l.Code)
            .ToListAsync(cancellationToken);

        var existingLeagueSet = new HashSet<string>(existingLeagueCodes, StringComparer.OrdinalIgnoreCase);
        bool addedAny = false;

        foreach (var seedLeague in BasketballLeague.GetDefaultSeedLeagues())
        {
            if (!existingLeagueSet.Contains(seedLeague.Code))
            {
                await _dbContext.Leagues.AddAsync(seedLeague, cancellationToken);
                addedAny = true;
            }
        }

        var existingSeasonYears = await _dbContext.Seasons
            .Select(s => s.EndYear)
            .ToListAsync(cancellationToken);

        var existingSeasonSet = new HashSet<int>(existingSeasonYears);

        foreach (var seedSeason in BasketballSeason.GetDefaultSeedSeasons())
        {
            if (!existingSeasonSet.Contains(seedSeason.EndYear))
            {
                await _dbContext.Seasons.AddAsync(seedSeason, cancellationToken);
                addedAny = true;
            }
        }

        if (addedAny)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
