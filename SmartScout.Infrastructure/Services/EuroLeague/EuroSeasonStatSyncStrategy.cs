using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Infrastructure.Services.EuroLeague;

public class EuroSeasonStatSyncStrategy : ISeasonStatSyncStrategy
{
    private readonly EuroLeagueApiClient _apiClient;
    private readonly ILogger<EuroSeasonStatSyncStrategy> _logger;

    public EuroSeasonStatSyncStrategy(EuroLeagueApiClient apiClient, ILogger<EuroSeasonStatSyncStrategy> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<SeasonStatSyncBatchResult> FetchSeasonStatsAsync(
        List<Player> players, 
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var euroPlayers = players
            .Where(p => string.Equals(p.League, "EuroLeague", StringComparison.OrdinalIgnoreCase) || 
                        string.Equals(p.League, "EuroCup", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!euroPlayers.Any())
        {
            yield break;
        }

        // Map by deterministic ID and by FullName
        var playerByExternalId = euroPlayers.ToDictionary(p => p.ExternalApiId);
        var playerByName = euroPlayers
            .GroupBy(p => $"{p.FirstName} {p.LastName}".Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        string[] seasons = { "U2024", "E2024" };
        var playerAccumulators = new Dictionary<Guid, EuroPlayerStatAccumulator>();

        foreach (var season in seasons)
        {
            if (cancellationToken.IsCancellationRequested) break;

            int consecutiveEmpty = 0;
            const int maxConsecutiveEmpty = 5;
            int maxGamesToScan = 220;

            for (int gameCode = 1; gameCode <= maxGamesToScan; gameCode++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var boxscore = await _apiClient.GetBoxscoreAsync(gameCode, season, cancellationToken);
                if (boxscore?.Stats == null || boxscore.Stats.Count == 0)
                {
                    consecutiveEmpty++;
                    if (consecutiveEmpty >= maxConsecutiveEmpty && gameCode > 20)
                    {
                        break;
                    }
                    continue;
                }

                consecutiveEmpty = 0;

                foreach (var teamStats in boxscore.Stats)
                {
                    if (teamStats.PlayersStats == null) continue;

                    foreach (var ps in teamStats.PlayersStats)
                    {
                        string? rawId = ps.Player_ID?.Trim();
                        string? rawName = ps.Player?.Trim();

                        if (string.IsNullOrWhiteSpace(rawId) || string.IsNullOrWhiteSpace(rawName))
                            continue;

                        int externalId = EuroLeagueParserHelper.GenerateDeterministicId($"Euro_{rawId}");
                        var (firstName, lastName) = EuroLeagueParserHelper.ParsePlayerName(rawName);
                        string fullName = $"{firstName} {lastName}".Trim();

                        Player? matchedPlayer = null;
                        if (playerByExternalId.TryGetValue(externalId, out var pById))
                        {
                            matchedPlayer = pById;
                        }
                        else if (playerByName.TryGetValue(fullName, out var pByName))
                        {
                            matchedPlayer = pByName;
                        }

                        if (matchedPlayer != null)
                        {
                            if (!playerAccumulators.TryGetValue(matchedPlayer.Id, out var accumulator))
                            {
                                accumulator = new EuroPlayerStatAccumulator(matchedPlayer.Id);
                                playerAccumulators[matchedPlayer.Id] = accumulator;
                            }

                            accumulator.Accumulate(ps);
                        }
                    }
                }
            }
        }

        // Build PlayerSeasonStat instances for all accumulated players
        var batchStats = new List<PlayerSeasonStat>();
        foreach (var acc in playerAccumulators.Values)
        {
            var stat = acc.ToPlayerSeasonStat(2024);
            if (stat != null)
            {
                batchStats.Add(stat);
            }
        }

        if (batchStats.Any())
        {
            _logger.LogInformation("Successfully parsed European season stats for {Count} players.", batchStats.Count);
            yield return new SeasonStatSyncBatchResult(batchStats, new List<Guid>());
        }
    }
}

public class EuroPlayerStatAccumulator
{
    public Guid PlayerId { get; }
    public int GamesPlayed { get; private set; }
    public decimal TotalMinutes { get; private set; }
    public int TotalPoints { get; private set; }
    public int TotalAssists { get; private set; }
    public int TotalRebounds { get; private set; }
    public int TotalSteals { get; private set; }
    public int TotalBlocks { get; private set; }
    public int TotalTurnovers { get; private set; }
    public int TotalFgm2 { get; private set; }
    public int TotalFga2 { get; private set; }
    public int TotalFgm3 { get; private set; }
    public int TotalFga3 { get; private set; }
    public int TotalFtm { get; private set; }
    public int TotalFta { get; private set; }
    public int TotalValuation { get; private set; }
    public int TotalFoulsReceived { get; private set; }

    public EuroPlayerStatAccumulator(Guid playerId)
    {
        PlayerId = playerId;
    }

    public void Accumulate(EuroPlayerGameStats ps)
    {
        decimal minutes = EuroLeagueParserHelper.ParseMinutes(ps.Minutes);
        if (minutes <= 0 && (ps.Points ?? 0) == 0 && (ps.Valuation ?? 0) == 0)
        {
            // DNP
            return;
        }

        GamesPlayed++;
        TotalMinutes += minutes;
        TotalPoints += ps.Points ?? 0;
        TotalAssists += ps.Assistances ?? 0;
        TotalRebounds += ps.TotalRebounds ?? 0;
        TotalSteals += ps.Steals ?? 0;
        TotalBlocks += ps.BlocksFavour ?? 0;
        TotalTurnovers += ps.Turnovers ?? 0;
        TotalFgm2 += ps.FieldGoalsMade2 ?? 0;
        TotalFga2 += ps.FieldGoalsAttempted2 ?? 0;
        TotalFgm3 += ps.FieldGoalsMade3 ?? 0;
        TotalFga3 += ps.FieldGoalsAttempted3 ?? 0;
        TotalFtm += ps.FreeThrowsMade ?? 0;
        TotalFta += ps.FreeThrowsAttempted ?? 0;
        TotalValuation += ps.Valuation ?? 0;
        TotalFoulsReceived += ps.FoulsReceived ?? 0;
    }

    public PlayerSeasonStat? ToPlayerSeasonStat(int season)
    {
        if (GamesPlayed == 0) return null;

        decimal gp = GamesPlayed;
        decimal minutes = Math.Round(TotalMinutes / gp, 1);
        decimal points = Math.Round(TotalPoints / gp, 1);
        decimal assists = Math.Round(TotalAssists / gp, 1);
        decimal rebounds = Math.Round(TotalRebounds / gp, 1);
        decimal steals = Math.Round(TotalSteals / gp, 1);
        decimal blocks = Math.Round(TotalBlocks / gp, 1);
        decimal turnovers = Math.Round(TotalTurnovers / gp, 1);
        decimal pir = Math.Round(TotalValuation / gp, 1);
        decimal foulsDrawn = Math.Round(TotalFoulsReceived / gp, 1);

        int totalFgm = TotalFgm2 + TotalFgm3;
        int totalFga = TotalFga2 + TotalFga3;

        decimal? fgPct = totalFga > 0 ? Math.Round((decimal)totalFgm / totalFga, 3) : null;
        decimal? fg3Pct = TotalFga3 > 0 ? Math.Round((decimal)TotalFgm3 / TotalFga3, 3) : null;
        decimal? ftPct = TotalFta > 0 ? Math.Round((decimal)TotalFtm / TotalFta, 3) : null;

        decimal? trueShootingPct = null;
        decimal tsAttempts = totalFga + 0.44m * TotalFta;
        if (tsAttempts > 0)
        {
            trueShootingPct = Math.Round(TotalPoints / (2m * tsAttempts), 3);
        }

        return new PlayerSeasonStat(
            playerId: PlayerId,
            season: season,
            gamesPlayed: GamesPlayed,
            minutes: minutes,
            points: points,
            assists: assists,
            rebounds: rebounds,
            steals: steals,
            blocks: blocks,
            fgPct: fgPct,
            fg3Pct: fg3Pct,
            ftPct: ftPct,
            turnovers: turnovers,
            trueShootingPct: trueShootingPct,
            playerEfficiencyRating: null, // European uses PIR
            usagePct: null,
            totalReboundPct: null,
            assistPct: null,
            winShares: null,
            boxPlusMinus: null,
            pir: pir,
            foulsDrawn: foulsDrawn
        );
    }
}
