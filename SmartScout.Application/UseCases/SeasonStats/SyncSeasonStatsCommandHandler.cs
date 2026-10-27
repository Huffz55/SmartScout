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

        // 1. Load completed checkpoints from IngestionJobLogs so strategies can skip already-completed (League, Season) jobs
        var completedLogs = await _dbContext.IngestionJobLogs
            .Where(l => l.Status == "Completed")
            .Select(l => new { l.LeagueCode, l.Season })
            .ToListAsync(cancellationToken);

        var completedSet = new HashSet<string>(
            completedLogs.Select(l => SyncFilterContext.BuildJobKey(l.LeagueCode, l.Season)),
            StringComparer.OrdinalIgnoreCase);

        int? parsedSeason = BasketballSeason.ParseSeasonEndYear(request.Season);
        var filterContext = new SyncFilterContext
        {
            LeagueCode = request.League,
            Season = parsedSeason,
            Force = request.Force,
            CompletedJobKeys = completedSet
        };

        var playersToFetch = await _dbContext.Players.ToListAsync(cancellationToken);

        foreach (var strategy in _syncStrategies)
        {
            await foreach (var batch in strategy.FetchSeasonStatsAsync(playersToFetch, cancellationToken, filterContext))
            {
                // 1. Physically delete phantom players if any were identified in this batch (SRP preserved)
                if (batch.PhantomPlayerIdsToDelete.Any())
                {
                    var phantomsToDelete = await _dbContext.Players
                        .Where(p => batch.PhantomPlayerIdsToDelete.Contains(p.Id))
                        .ToListAsync(cancellationToken);

                    if (phantomsToDelete.Any())
                    {
                        _dbContext.Players.RemoveRange(phantomsToDelete);
                    }
                }

                // 2. Ensure any newly discovered players in this batch exist in DB before inserting their stats
                var playerIdRemap = new Dictionary<Guid, Guid>();
                if (batch.DiscoveredPlayers.Any())
                {
                    var discoveredIds = batch.DiscoveredPlayers.Select(p => p.ExternalApiId).Distinct().ToList();
                    var discoveredNormNames = batch.DiscoveredPlayers
                        .Select(p => p.NormalizedName)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct()
                        .ToList();

                    var existingDbPlayers = await _dbContext.Players
                        .Where(p => discoveredIds.Contains(p.ExternalApiId) || discoveredNormNames.Contains(p.NormalizedName))
                        .ToListAsync(cancellationToken);

                    var dbByExternalId = existingDbPlayers
                        .GroupBy(p => p.ExternalApiId)
                        .ToDictionary(g => g.Key, g => g.First());

                    var dbByNormName = existingDbPlayers
                        .Where(p => !string.IsNullOrWhiteSpace(p.NormalizedName))
                        .GroupBy(p => p.NormalizedName, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    foreach (var discPlayer in batch.DiscoveredPlayers)
                    {
                        Player? existingDbPlayer = null;
                        if (!string.IsNullOrWhiteSpace(discPlayer.NormalizedName) &&
                            dbByNormName.TryGetValue(discPlayer.NormalizedName, out var byName))
                        {
                            existingDbPlayer = byName;
                        }
                        else if (dbByExternalId.TryGetValue(discPlayer.ExternalApiId, out var byId))
                        {
                            existingDbPlayer = byId;
                        }

                        if (existingDbPlayer != null)
                        {
                            playerIdRemap[discPlayer.Id] = existingDbPlayer.Id;

                            string? teamToSet = (!string.IsNullOrWhiteSpace(discPlayer.TeamName) && !discPlayer.TeamName.EndsWith("Team", StringComparison.OrdinalIgnoreCase))
                                ? discPlayer.TeamName
                                : existingDbPlayer.TeamName;

                            string? posToSet = !string.IsNullOrWhiteSpace(discPlayer.Position)
                                ? discPlayer.Position
                                : existingDbPlayer.Position;

                            string? natToSet = !string.IsNullOrWhiteSpace(discPlayer.Nationality)
                                ? discPlayer.Nationality
                                : existingDbPlayer.Nationality;

                            // Preserve existing real birth date if already known
                            DateTime? birthToSet = existingDbPlayer.BirthDate ?? discPlayer.BirthDate;

                            existingDbPlayer.UpdateDetails(
                                firstName: !string.IsNullOrWhiteSpace(discPlayer.FirstName) ? discPlayer.FirstName : existingDbPlayer.FirstName,
                                lastName: !string.IsNullOrWhiteSpace(discPlayer.LastName) ? discPlayer.LastName : existingDbPlayer.LastName,
                                position: posToSet,
                                height: discPlayer.Height ?? existingDbPlayer.Height,
                                weightPounds: discPlayer.WeightPounds ?? existingDbPlayer.WeightPounds,
                                teamName: teamToSet,
                                league: !string.IsNullOrWhiteSpace(discPlayer.League) ? discPlayer.League : existingDbPlayer.League,
                                nationality: natToSet,
                                birthDate: birthToSet,
                                externalIdsJson: discPlayer.ExternalIdsJson ?? existingDbPlayer.ExternalIdsJson);
                        }
                        else
                        {
                            await _dbContext.Players.AddAsync(discPlayer, cancellationToken);
                            dbByExternalId[discPlayer.ExternalApiId] = discPlayer;
                            if (!string.IsNullOrWhiteSpace(discPlayer.NormalizedName))
                            {
                                dbByNormName[discPlayer.NormalizedName] = discPlayer;
                            }
                            playersToFetch.Add(discPlayer);
                            playerIdRemap[discPlayer.Id] = discPlayer.Id;
                        }
                    }
                }

                // 3. Process and upsert season stats by full Stint key: (PlayerId, Season, League, TeamName, CompetitionPhase)
                var batchList = batch.Stats.ToList();
                if (batchList.Any())
                {
                    var resolvedPlayerIds = batchList
                        .Select(s => playerIdRemap.TryGetValue(s.PlayerId, out var mappedId) ? mappedId : s.PlayerId)
                        .Distinct()
                        .ToList();

                    var existingStatsList = await _dbContext.SeasonStats
                        .Where(s => resolvedPlayerIds.Contains(s.PlayerId))
                        .ToListAsync(cancellationToken);

                    var existingLookup = existingStatsList
                        .GroupBy(s => BuildStintKey(s.PlayerId, s.Season, s.League, s.TeamName, s.CompetitionPhase))
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    foreach (var incomingStat in batchList)
                    {
                        Guid targetPlayerId = playerIdRemap.TryGetValue(incomingStat.PlayerId, out var mapped)
                            ? mapped
                            : incomingStat.PlayerId;

                        string key = BuildStintKey(
                            targetPlayerId,
                            incomingStat.Season,
                            incomingStat.League,
                            incomingStat.TeamName,
                            incomingStat.CompetitionPhase);

                        PlayerSeasonStat? targetStat = null;
                        if (existingLookup.TryGetValue(key, out var existingStat))
                        {
                            targetStat = existingStat;
                        }
                        else
                        {
                            // Check if an existing stint for this player/season/league had a placeholder team name (e.g. "BSL Team", "{league} Team", or "TOT")
                            var placeholderStat = existingStatsList.FirstOrDefault(s =>
                                s.PlayerId == targetPlayerId &&
                                s.Season == incomingStat.Season &&
                                s.League.Equals(incomingStat.League, StringComparison.OrdinalIgnoreCase) &&
                                s.CompetitionPhase.Equals(incomingStat.CompetitionPhase, StringComparison.OrdinalIgnoreCase) &&
                                (s.TeamName.EndsWith("Team", StringComparison.OrdinalIgnoreCase) || s.TeamName.Equals("TOT", StringComparison.OrdinalIgnoreCase)));

                            if (placeholderStat != null)
                            {
                                targetStat = placeholderStat;
                                string oldKey = BuildStintKey(placeholderStat.PlayerId, placeholderStat.Season, placeholderStat.League, placeholderStat.TeamName, placeholderStat.CompetitionPhase);
                                existingLookup.Remove(oldKey);
                                existingLookup[key] = placeholderStat;
                            }
                        }

                        if (targetStat != null)
                        {
                            targetStat.UpdateStats(
                                gamesPlayed: incomingStat.GamesPlayed,
                                minutes: incomingStat.Minutes,
                                points: incomingStat.Points,
                                assists: incomingStat.Assists,
                                rebounds: incomingStat.Rebounds,
                                steals: incomingStat.Steals,
                                blocks: incomingStat.Blocks,
                                fgPct: incomingStat.FgPct,
                                fg3Pct: incomingStat.Fg3Pct,
                                ftPct: incomingStat.FtPct,
                                turnovers: incomingStat.Turnovers,
                                trueShootingPct: incomingStat.TrueShootingPct,
                                playerEfficiencyRating: incomingStat.PlayerEfficiencyRating,
                                usagePct: incomingStat.UsagePct,
                                totalReboundPct: incomingStat.TotalReboundPct,
                                assistPct: incomingStat.AssistPct,
                                winShares: incomingStat.WinShares,
                                boxPlusMinus: incomingStat.BoxPlusMinus,
                                pir: incomingStat.Pir,
                                foulsDrawn: incomingStat.FoulsDrawn,
                                pointsPer40: incomingStat.PointsPer40,
                                assistsPer40: incomingStat.AssistsPer40,
                                reboundsPer40: incomingStat.ReboundsPer40,
                                league: incomingStat.League,
                                teamName: incomingStat.TeamName,
                                competitionPhase: incomingStat.CompetitionPhase,
                                gamesStarted: incomingStat.GamesStarted,
                                minutesPlayedTotal: incomingStat.MinutesPlayedTotal,
                                fgm: incomingStat.FieldGoalsMade,
                                fga: incomingStat.FieldGoalsAttempted,
                                fg3m: incomingStat.ThreePointsMade,
                                fg3a: incomingStat.ThreePointsAttempted,
                                fg2m: incomingStat.TwoPointsMade,
                                fg2a: incomingStat.TwoPointsAttempted,
                                fg2Pct: incomingStat.Fg2Pct,
                                ftm: incomingStat.FreeThrowsMade,
                                fta: incomingStat.FreeThrowsAttempted,
                                orb: incomingStat.OffensiveRebounds,
                                drb: incomingStat.DefensiveRebounds,
                                pfCommitted: incomingStat.PersonalFoulsCommitted,
                                plusMinus: incomingStat.PlusMinus,
                                effectiveFgPct: incomingStat.EffectiveFgPct,
                                assistToTurnoverRatio: incomingStat.AssistToTurnoverRatio,
                                gameScoreAverage: incomingStat.GameScoreAverage,
                                rawScrapedPayload: incomingStat.RawScrapedPayload
                            );
                        }
                        else
                        {
                            var statToInsert = targetPlayerId == incomingStat.PlayerId
                                ? incomingStat
                                : new PlayerSeasonStat(
                                    playerId: targetPlayerId,
                                    season: incomingStat.Season,
                                    gamesPlayed: incomingStat.GamesPlayed,
                                    minutes: incomingStat.Minutes,
                                    points: incomingStat.Points,
                                    assists: incomingStat.Assists,
                                    rebounds: incomingStat.Rebounds,
                                    steals: incomingStat.Steals,
                                    blocks: incomingStat.Blocks,
                                    fgPct: incomingStat.FgPct,
                                    fg3Pct: incomingStat.Fg3Pct,
                                    ftPct: incomingStat.FtPct,
                                    turnovers: incomingStat.Turnovers,
                                    trueShootingPct: incomingStat.TrueShootingPct,
                                    playerEfficiencyRating: incomingStat.PlayerEfficiencyRating,
                                    usagePct: incomingStat.UsagePct,
                                    totalReboundPct: incomingStat.TotalReboundPct,
                                    assistPct: incomingStat.AssistPct,
                                    winShares: incomingStat.WinShares,
                                    boxPlusMinus: incomingStat.BoxPlusMinus,
                                    pir: incomingStat.Pir,
                                    foulsDrawn: incomingStat.FoulsDrawn,
                                    pointsPer40: incomingStat.PointsPer40,
                                    assistsPer40: incomingStat.AssistsPer40,
                                    reboundsPer40: incomingStat.ReboundsPer40,
                                    league: incomingStat.League,
                                    teamName: incomingStat.TeamName,
                                    competitionPhase: incomingStat.CompetitionPhase,
                                    gamesStarted: incomingStat.GamesStarted,
                                    minutesPlayedTotal: incomingStat.MinutesPlayedTotal,
                                    fgm: incomingStat.FieldGoalsMade,
                                    fga: incomingStat.FieldGoalsAttempted,
                                    fg3m: incomingStat.ThreePointsMade,
                                    fg3a: incomingStat.ThreePointsAttempted,
                                    fg2m: incomingStat.TwoPointsMade,
                                    fg2a: incomingStat.TwoPointsAttempted,
                                    fg2Pct: incomingStat.Fg2Pct,
                                    ftm: incomingStat.FreeThrowsMade,
                                    fta: incomingStat.FreeThrowsAttempted,
                                    orb: incomingStat.OffensiveRebounds,
                                    drb: incomingStat.DefensiveRebounds,
                                    pfCommitted: incomingStat.PersonalFoulsCommitted,
                                    plusMinus: incomingStat.PlusMinus,
                                    effectiveFgPct: incomingStat.EffectiveFgPct,
                                    assistToTurnoverRatio: incomingStat.AssistToTurnoverRatio,
                                    gameScoreAverage: incomingStat.GameScoreAverage,
                                    rawScrapedPayload: incomingStat.RawScrapedPayload);

                            await _dbContext.SeasonStats.AddAsync(statToInsert, cancellationToken);
                            existingLookup[key] = statToInsert;
                        }
                        
                        totalUpsertedCount++;
                    }
                }

                // 4. Record IngestionJobLog checkpoint per (LeagueCode, Season) and immediately persist
                if (!string.IsNullOrWhiteSpace(batch.LeagueCode) && batch.Season.HasValue)
                {
                    var existingLog = await _dbContext.IngestionJobLogs
                        .FirstOrDefaultAsync(l => l.LeagueCode == batch.LeagueCode && l.Season == batch.Season.Value, cancellationToken);

                    if (existingLog == null)
                    {
                        existingLog = new IngestionJobLog(batch.LeagueCode, batch.Season.Value, batch.JobStatus, batchList.Count, batch.ErrorMessage);
                        await _dbContext.IngestionJobLogs.AddAsync(existingLog, cancellationToken);
                    }
                    else if (batch.JobStatus == "Failed")
                    {
                        existingLog.MarkFailed(batch.ErrorMessage ?? "Unknown scraping error");
                    }
                    else
                    {
                        existingLog.MarkCompleted(batchList.Count);
                    }
                }

                if (batch.PhantomPlayerIdsToDelete.Any() || batch.DiscoveredPlayers.Any() || batchList.Any() || !string.IsNullOrWhiteSpace(batch.LeagueCode))
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }

        return totalUpsertedCount;
    }

    private static string BuildStintKey(Guid playerId, int season, string? league, string? teamName, string? phase)
    {
        string l = string.IsNullOrWhiteSpace(league) ? "NBA" : league.Trim();
        string t = string.IsNullOrWhiteSpace(teamName) ? "TOT" : teamName.Trim();
        string p = string.IsNullOrWhiteSpace(phase) ? "Regular Season" : phase.Trim();
        return $"{playerId:N}|{season}|{l}|{t}|{p}";
    }
}
