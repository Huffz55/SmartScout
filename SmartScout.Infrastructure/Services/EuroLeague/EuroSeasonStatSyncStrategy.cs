using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Options;

namespace SmartScout.Infrastructure.Services.EuroLeague;

public class EuroSeasonStatSyncStrategy : ISeasonStatSyncStrategy
{
    private readonly EuroLeagueApiClient _apiClient;
    private readonly ScraperOptions _options;
    private readonly ILogger<EuroSeasonStatSyncStrategy> _logger;

    public EuroSeasonStatSyncStrategy(
        EuroLeagueApiClient apiClient, 
        IOptions<ScraperOptions> options,
        ILogger<EuroSeasonStatSyncStrategy> logger)
    {
        _apiClient = apiClient;
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<SeasonStatSyncBatchResult> FetchSeasonStatsAsync(
        List<Player> players, 
        [EnumeratorCancellation] CancellationToken cancellationToken,
        SyncFilterContext? filter = null)
    {
        // EuroLeague and EuroCup are fast-scraped via GlobalLeagueTableScrapingStrategy (3s vs 10min) unless boxscore API is explicitly enabled
        if (!_options.EnableEuroLiveApiBoxscores)
        {
            _logger.LogInformation("Skipping EuroSeasonStatSyncStrategy (live.euroleague.net boxscore API). EuroLeague and EuroCup stats are fast-synced via GlobalLeagueTableScrapingStrategy.");
            yield break;
        }

        if (filter != null &&
            !filter.ShouldProcessLeague("EuroLeague") &&
            !filter.ShouldProcessLeague("EuroCup"))
        {
            yield break;
        }

        // Map all players by deterministic ID and by NormalizedName so players who crossed over between NBA, EuroCup, and EuroLeague are matched
        var playerByExternalId = players
            .GroupBy(p => p.ExternalApiId)
            .ToDictionary(g => g.Key, g => g.First());

        var playerByNormalizedName = players
            .GroupBy(p => !string.IsNullOrWhiteSpace(p.NormalizedName) ? p.NormalizedName : Player.NormalizeName(p.FirstName, p.LastName), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seasons = (_options.EuroSeasons?.Any() == true ? _options.EuroSeasons : new[] { 2019, 2020, 2021, 2022, 2023, 2024, 2025, 2026 })
            .Where(y => filter == null || filter.ShouldProcessSeason(y))
            .OrderBy(y => y)
            .ToArray();

        int batchConcurrency = Math.Clamp(_options.EuroMaxConcurrentRequests, 1, 3);
        int waveDelayMs = Math.Max(1200, _options.EuroDelayBetweenWavesMs);
        const int maxGamesPerSeason = 340;

        foreach (int year in seasons)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string[] competitionCodes = { $"U{year}", $"E{year}" };

            foreach (string seasonCode in competitionCodes)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string league = seasonCode.StartsWith("U", StringComparison.OrdinalIgnoreCase) ? "EuroCup" : "EuroLeague";

                if (filter != null && !filter.ShouldProcessLeague(league))
                {
                    continue;
                }

                if (filter != null && filter.IsAlreadyCompleted(league, year))
                {
                    _logger.LogInformation("Skipping {League} season {Year} — already marked Completed in IngestionJobLogs.", league, year);
                    continue;
                }

                // Keyed by Stint: (PlayerId, League, TeamName) to support multi-team & multi-competition split seasons
                var competitionAccumulators = new Dictionary<(Guid PlayerId, string League, string TeamName), EuroPlayerStatAccumulator>();
                var discoveredPlayers = new List<Player>();

                _logger.LogInformation("Aggregating {League} ({SeasonCode}) boxscore statistics for season {Year}...", league, seasonCode, year);

                int consecutiveEmptyChunks = 0;
                for (int startGame = 1; startGame <= maxGamesPerSeason; startGame += batchConcurrency)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    int count = Math.Min(batchConcurrency, maxGamesPerSeason - startGame + 1);
                    var tasks = Enumerable.Range(startGame, count)
                        .Select(gameCode => _apiClient.GetBoxscoreAsync(gameCode, seasonCode, cancellationToken))
                        .ToArray();

                    var boxscores = await Task.WhenAll(tasks);

                    int validGamesInChunk = 0;

                    foreach (var boxscore in boxscores)
                    {
                        if (boxscore?.Stats == null || boxscore.Stats.Count == 0)
                            continue;

                        validGamesInChunk++;

                        foreach (var teamStats in boxscore.Stats)
                        {
                            if (teamStats.PlayersStats == null) continue;

                            string teamName = teamStats.Team?.Trim() ?? $"{league} Team";

                            foreach (var ps in teamStats.PlayersStats)
                            {
                                string? rawId = ps.Player_ID?.Trim();
                                string? rawName = ps.Player?.Trim();

                                if (string.IsNullOrWhiteSpace(rawId) || string.IsNullOrWhiteSpace(rawName))
                                    continue;

                                int externalId = EuroLeagueParserHelper.GenerateDeterministicId($"Euro_{rawId}");
                                var (firstName, lastName) = EuroLeagueParserHelper.ParsePlayerName(rawName);
                                string normalizedName = Player.NormalizeName(firstName, lastName);

                                Player? matchedPlayer = null;
                                if (playerByExternalId.TryGetValue(externalId, out var pById))
                                {
                                    matchedPlayer = pById;
                                }
                                else if (!string.IsNullOrWhiteSpace(normalizedName) && playerByNormalizedName.TryGetValue(normalizedName, out var pByName))
                                {
                                    matchedPlayer = pByName;
                                }

                                // Auto-discover player if sync-stats was called directly for a historical season before player sync
                                if (matchedPlayer == null)
                                {
                                    string? nationality = EuroLeagueParserHelper.DetectNationality(firstName, lastName, teamName);
                                    string externalIdsJson = JsonSerializer.Serialize(new { euroleagueId = rawId, latestSeasonCode = seasonCode });
                                    matchedPlayer = new Player(
                                        externalApiId: externalId,
                                        firstName: firstName,
                                        lastName: lastName,
                                        position: null,
                                        height: null,
                                        weightPounds: null,
                                        teamName: teamName,
                                        league: league,
                                        nationality: nationality,
                                        externalIdsJson: externalIdsJson);

                                    playerByExternalId[externalId] = matchedPlayer;
                                    if (!string.IsNullOrWhiteSpace(normalizedName))
                                    {
                                        playerByNormalizedName[normalizedName] = matchedPlayer;
                                    }
                                    discoveredPlayers.Add(matchedPlayer);
                                }

                                var stintKey = (matchedPlayer.Id, league, teamName);
                                if (!competitionAccumulators.TryGetValue(stintKey, out var accumulator))
                                {
                                    accumulator = new EuroPlayerStatAccumulator(matchedPlayer.Id, league, teamName);
                                    competitionAccumulators[stintKey] = accumulator;
                                }

                                accumulator.Accumulate(ps, league, teamName);
                            }
                        }
                    }

                    if (validGamesInChunk == 0)
                    {
                        consecutiveEmptyChunks++;
                        if (consecutiveEmptyChunks >= 3)
                        {
                            break;
                        }
                    }
                    else
                    {
                        consecutiveEmptyChunks = 0;
                    }

                    await Task.Delay(waveDelayMs, cancellationToken);
                }

                var seasonStatsBatch = new List<PlayerSeasonStat>();
                foreach (var acc in competitionAccumulators.Values)
                {
                    var stat = acc.ToPlayerSeasonStat(year);
                    if (stat != null)
                    {
                        seasonStatsBatch.Add(stat);
                    }
                }

                if (seasonStatsBatch.Any())
                {
                    _logger.LogInformation("Successfully aggregated {Count} player stint records for {League} Season {Year}.",
                        seasonStatsBatch.Count, league, year);

                    yield return new SeasonStatSyncBatchResult(
                        stats: seasonStatsBatch,
                        phantomPlayerIdsToDelete: Array.Empty<Guid>(),
                        leagueCode: league,
                        season: year,
                        discoveredPlayers: discoveredPlayers,
                        jobStatus: "Completed");
                }
            }
        }
    }
}

public class EuroPlayerStatAccumulator
{
    public Guid PlayerId { get; }
    public string League { get; private set; }
    public string TeamName { get; private set; }
    public int GamesPlayed { get; private set; }
    public int GamesStarted { get; private set; }
    public decimal TotalMinutes { get; private set; }
    public int TotalPoints { get; private set; }
    public int TotalAssists { get; private set; }
    public int TotalOffensiveRebounds { get; private set; }
    public int TotalDefensiveRebounds { get; private set; }
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
    public int TotalFoulsCommitted { get; private set; }
    public int TotalFoulsReceived { get; private set; }
    public int TotalPlusMinus { get; private set; }

    public EuroPlayerStatAccumulator(Guid playerId, string league, string teamName)
    {
        PlayerId = playerId;
        League = league;
        TeamName = teamName;
    }

    public void Accumulate(EuroPlayerGameStats ps, string? league = null, string? teamName = null)
    {
        decimal minutes = EuroLeagueParserHelper.ParseMinutes(ps.Minutes);
        if (minutes <= 0 && (ps.Points ?? 0) == 0 && (ps.Valuation ?? 0) == 0)
        {
            // DNP
            return;
        }

        if (!string.IsNullOrWhiteSpace(league)) League = league;
        if (!string.IsNullOrWhiteSpace(teamName)) TeamName = teamName;

        GamesPlayed++;
        if (ps.IsStarter == 1) GamesStarted++;
        TotalMinutes += minutes;
        TotalPoints += ps.Points ?? 0;
        TotalAssists += ps.Assistances ?? 0;
        TotalOffensiveRebounds += ps.OffensiveRebounds ?? 0;
        TotalDefensiveRebounds += ps.DefensiveRebounds ?? 0;
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
        TotalFoulsCommitted += ps.FoulsCommited ?? 0;
        TotalFoulsReceived += ps.FoulsReceived ?? 0;
        TotalPlusMinus += ps.Plusminus ?? 0;
    }

    public PlayerSeasonStat? ToPlayerSeasonStat(int season)
    {
        if (GamesPlayed == 0) return null;

        decimal gp = GamesPlayed;
        decimal minutes = Math.Round(TotalMinutes / gp, 1);
        decimal points = Math.Round(TotalPoints / gp, 1);
        decimal assists = Math.Round(TotalAssists / gp, 1);
        decimal orb = Math.Round(TotalOffensiveRebounds / gp, 1);
        decimal drb = Math.Round(TotalDefensiveRebounds / gp, 1);
        decimal rebounds = Math.Round(TotalRebounds / gp, 1);
        decimal steals = Math.Round(TotalSteals / gp, 1);
        decimal blocks = Math.Round(TotalBlocks / gp, 1);
        decimal turnovers = Math.Round(TotalTurnovers / gp, 1);
        decimal pir = Math.Round(TotalValuation / gp, 1);
        decimal pfCommitted = Math.Round(TotalFoulsCommitted / gp, 1);
        decimal foulsDrawn = Math.Round(TotalFoulsReceived / gp, 1);
        decimal plusMinus = Math.Round(TotalPlusMinus / gp, 1);

        int totalFgm = TotalFgm2 + TotalFgm3;
        int totalFga = TotalFga2 + TotalFga3;

        decimal fgmPerGame = Math.Round(totalFgm / gp, 1);
        decimal fgaPerGame = Math.Round(totalFga / gp, 1);
        decimal fg3mPerGame = Math.Round(TotalFgm3 / gp, 1);
        decimal fg3aPerGame = Math.Round(TotalFga3 / gp, 1);
        decimal fg2mPerGame = Math.Round(TotalFgm2 / gp, 1);
        decimal fg2aPerGame = Math.Round(TotalFga2 / gp, 1);
        decimal ftmPerGame = Math.Round(TotalFtm / gp, 1);
        decimal ftaPerGame = Math.Round(TotalFta / gp, 1);

        decimal? fgPct = totalFga > 0 ? Math.Round((decimal)totalFgm / totalFga, 3) : null;
        decimal? fg3Pct = TotalFga3 > 0 ? Math.Round((decimal)TotalFgm3 / TotalFga3, 3) : null;
        decimal? fg2Pct = TotalFga2 > 0 ? Math.Round((decimal)TotalFgm2 / TotalFga2, 3) : null;
        decimal? ftPct = TotalFta > 0 ? Math.Round((decimal)TotalFtm / TotalFta, 3) : null;

        string rawJson = JsonSerializer.Serialize(new
        {
            source = "EuroLeagueLiveApi",
            season,
            league = League,
            team = TeamName,
            gamesPlayed = GamesPlayed,
            gamesStarted = GamesStarted,
            totals = new
            {
                minutes = Math.Round(TotalMinutes, 1),
                pts = TotalPoints,
                ast = TotalAssists,
                orb = TotalOffensiveRebounds,
                drb = TotalDefensiveRebounds,
                trb = TotalRebounds,
                stl = TotalSteals,
                blk = TotalBlocks,
                tov = TotalTurnovers,
                fgm2 = TotalFgm2,
                fga2 = TotalFga2,
                fgm3 = TotalFgm3,
                fga3 = TotalFga3,
                ftm = TotalFtm,
                fta = TotalFta,
                pir = TotalValuation,
                pf = TotalFoulsCommitted,
                pfd = TotalFoulsReceived,
                plusMinus = TotalPlusMinus
            }
        });

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
            trueShootingPct: null,
            playerEfficiencyRating: null,
            usagePct: null,
            totalReboundPct: null,
            assistPct: null,
            winShares: null,
            boxPlusMinus: null,
            pir: pir,
            foulsDrawn: foulsDrawn,
            league: League,
            teamName: TeamName,
            competitionPhase: "Regular Season",
            gamesStarted: GamesStarted,
            minutesPlayedTotal: Math.Round(TotalMinutes, 1),
            fgm: fgmPerGame,
            fga: fgaPerGame,
            fg3m: fg3mPerGame,
            fg3a: fg3aPerGame,
            fg2m: fg2mPerGame,
            fg2a: fg2aPerGame,
            fg2Pct: fg2Pct,
            ftm: ftmPerGame,
            fta: ftaPerGame,
            orb: orb,
            drb: drb,
            pfCommitted: pfCommitted,
            plusMinus: plusMinus,
            rawScrapedPayload: rawJson
        );
    }
}
