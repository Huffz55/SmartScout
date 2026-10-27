using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Infrastructure.Services;

public class BalldontlieSyncStrategy : IPlayerSyncStrategy
{
    private readonly HttpClient _httpClient;

    public BalldontlieSyncStrategy(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async IAsyncEnumerable<IEnumerable<Player>> FetchPlayersAsync(
        int cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        SyncFilterContext? filter = null)
    {
        // NBA players sync runs only when explicitly targeted (e.g. ?league=NBA), not during general all-league sweeps
        if (filter != null && (!filter.ShouldProcessLeague("NBA") || (filter.IsAllLeagues && !filter.HasExplicitLeague("NBA"))))
        {
            yield break;
        }

        int? currentCursor = cursor > 0 ? cursor : null;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        while (!cancellationToken.IsCancellationRequested)
        {
            string url = currentCursor.HasValue ? $"?cursor={currentCursor.Value}" : "";
            
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                break;
            }

            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<BalldontlieResponseDto>(contentStream, options, cancellationToken);

            var dtos = result?.Data ?? new List<BalldontliePlayerDto>();

            var mappedPlayers = dtos.Select(dto => 
            {
                int? weightPounds = int.TryParse(dto.Weight, out int parsedWeight) ? parsedWeight : (int?)null;
                return new Player(
                    dto.Id,
                    dto.FirstName,
                    dto.LastName,
                    dto.Position,
                    dto.Height,
                    weightPounds,
                    dto.Team?.FullName,
                    "NBA"
                );
            }).ToList();

            if (mappedPlayers.Any())
            {
                yield return mappedPlayers;
            }

            if (result?.Meta?.NextCursor == null)
            {
                break;
            }

            currentCursor = result.Meta.NextCursor.Value;
        }
    }
}
