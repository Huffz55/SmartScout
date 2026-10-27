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
using SmartScout.Infrastructure.Services.EuroLeague;

namespace SmartScout.Infrastructure.Services;

public class EuroScrapingSyncStrategy : IPlayerSyncStrategy
{
    private readonly EuroLeagueApiClient _apiClient;
    private readonly ScraperOptions _options;
    private readonly ILogger<EuroScrapingSyncStrategy> _logger;

    public EuroScrapingSyncStrategy(
        EuroLeagueApiClient apiClient, 
        IOptions<ScraperOptions> options,
        ILogger<EuroScrapingSyncStrategy> logger)
    {
        _apiClient = apiClient;
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<IEnumerable<Player>> FetchPlayersAsync(
        int cursor, 
        [EnumeratorCancellation] CancellationToken cancellationToken,
        SyncFilterContext? filter = null)
    {
        // EuroLeague and EuroCup rosters are fast-scraped via GlobalLeagueTableScrapingStrategy unless live boxscore API is explicitly enabled
        if (!_options.EnableEuroLiveApiBoxscores)
        {
            _logger.LogInformation("Skipping EuroScrapingSyncStrategy (live.euroleague.net boxscore API). EuroLeague and EuroCup rosters are discovered via GlobalLeagueTableScrapingStrategy.");
            yield break;
        }

        if (filter != null &&
            !filter.ShouldProcessLeague("EuroLeague") &&
            !filter.ShouldProcessLeague("EuroCup"))
        {
            yield break;
        }

        var seasons = (_options.EuroSeasons?.Any() == true ? _options.EuroSeasons : new[] { 2019, 2020, 2021, 2022, 2023, 2024, 2025, 2026 })
            .Where(y => filter == null || filter.ShouldProcessSeason(y))
            .OrderBy(y => y)
            .ToArray();

        int batchConcurrency = Math.Clamp(_options.EuroMaxConcurrentRequests, 1, 3);
        int waveDelayMs = Math.Max(1200, _options.EuroDelayBetweenWavesMs);
        const int maxGamesPerSeason = 240;

        foreach (int year in seasons)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Scan EuroCup (U) then EuroLeague (E) for each year in chronological order
            // so the most recent season & highest tier updates the Player entity's current TeamName and League
            string[] competitionCodes = { $"U{year}", $"E{year}" };

            foreach (string seasonCode in competitionCodes)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string league = seasonCode.StartsWith("U", StringComparison.OrdinalIgnoreCase) ? "EuroCup" : "EuroLeague";

                if (filter != null && !filter.ShouldProcessLeague(league))
                {
                    continue;
                }

                var discoveredInCompetition = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pendingBatch = new List<Player>();

                _logger.LogInformation("Scanning {League} ({SeasonCode}) rosters...", league, seasonCode);

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

                                if (discoveredInCompetition.Add($"{rawId}_{teamName}"))
                                {
                                    var (firstName, lastName) = EuroLeagueParserHelper.ParsePlayerName(rawName);
                                    string? nationality = EuroLeagueParserHelper.DetectNationality(firstName, lastName, teamName);
                                    int externalId = EuroLeagueParserHelper.GenerateDeterministicId($"Euro_{rawId}");
                                    string externalIdsJson = JsonSerializer.Serialize(new { euroleagueId = rawId, latestSeasonCode = seasonCode });

                                    var player = new Player(
                                        externalApiId: externalId,
                                        firstName: firstName,
                                        lastName: lastName,
                                        position: null,
                                        height: null,
                                        weightPounds: null,
                                        teamName: teamName,
                                        league: league,
                                        nationality: nationality,
                                        externalIdsJson: externalIdsJson
                                    );

                                    pendingBatch.Add(player);

                                    if (pendingBatch.Count >= 30)
                                    {
                                        yield return pendingBatch.ToArray();
                                        pendingBatch.Clear();
                                    }
                                }
                            }
                        }
                    }

                    if (validGamesInChunk == 0)
                    {
                        consecutiveEmptyChunks++;
                        if (consecutiveEmptyChunks >= 3)
                        {
                            _logger.LogInformation("Completed {League} ({SeasonCode}) scan at game #{GameCode} ({PlayerCount} player-team entries found).", 
                                league, seasonCode, startGame, discoveredInCompetition.Count);
                            break;
                        }
                    }
                    else
                    {
                        consecutiveEmptyChunks = 0;
                    }

                    await Task.Delay(waveDelayMs, cancellationToken);
                }

                if (pendingBatch.Count > 0)
                {
                    yield return pendingBatch.ToArray();
                    pendingBatch.Clear();
                }
            }
        }
    }
}
