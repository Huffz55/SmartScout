using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Services.EuroLeague;

namespace SmartScout.Infrastructure.Services;

public class EuroScrapingSyncStrategy : IPlayerSyncStrategy
{
    private readonly EuroLeagueApiClient _apiClient;
    private readonly ILogger<EuroScrapingSyncStrategy> _logger;

    public EuroScrapingSyncStrategy(EuroLeagueApiClient apiClient, ILogger<EuroScrapingSyncStrategy> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<IEnumerable<Player>> FetchPlayersAsync(
        int cursor, 
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var discoveredPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingBatch = new List<Player>();

        // Competitions: U2024 (EuroCup - Beşiktaş Fibabanka, etc.) & E2024 (EuroLeague)
        string[] seasons = { "U2024", "E2024" };

        foreach (var season in seasons)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string league = season.StartsWith("U", StringComparison.OrdinalIgnoreCase) ? "EuroCup" : "EuroLeague";
            int consecutiveEmptyGames = 0;
            const int maxConsecutiveEmpty = 5;
            int maxGamesToScan = 220; // Full regular season & playoffs span

            for (int gameCode = 1; gameCode <= maxGamesToScan; gameCode++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var boxscore = await _apiClient.GetBoxscoreAsync(gameCode, season, cancellationToken);
                if (boxscore?.Stats == null || boxscore.Stats.Count == 0)
                {
                    consecutiveEmptyGames++;
                    if (consecutiveEmptyGames >= maxConsecutiveEmpty && gameCode > 20)
                    {
                        _logger.LogInformation("Finished scanning {League} ({Season}) at game #{GameCode}", league, season, gameCode);
                        break;
                    }
                    continue;
                }

                consecutiveEmptyGames = 0;

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

                        if (discoveredPlayerIds.Add(rawId))
                        {
                            var (firstName, lastName) = EuroLeagueParserHelper.ParsePlayerName(rawName);
                            string? nationality = EuroLeagueParserHelper.DetectNationality(firstName, lastName, teamName);
                            int externalId = EuroLeagueParserHelper.GenerateDeterministicId($"Euro_{rawId}");

                            var player = new Player(
                                externalApiId: externalId,
                                firstName: firstName,
                                lastName: lastName,
                                position: null,
                                height: null,
                                weightPounds: null,
                                teamName: teamName,
                                league: league,
                                nationality: nationality
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
        }

        if (pendingBatch.Count > 0)
        {
            yield return pendingBatch.ToArray();
        }
    }
}
