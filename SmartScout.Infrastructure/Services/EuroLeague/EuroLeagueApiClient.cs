using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SmartScout.Infrastructure.Services.EuroLeague;

public class EuroLeagueApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EuroLeagueApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EuroLeagueApiClient(HttpClient httpClient, ILogger<EuroLeagueApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<EuroBoxscoreResponse?> GetBoxscoreAsync(int gameCode, string seasonCode, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"Boxscore?gamecode={gameCode}&seasoncode={seasonCode}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<EuroBoxscoreResponse>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch EuroLeague/EuroCup boxscore for game {GameCode}, season {SeasonCode}", gameCode, seasonCode);
            return null;
        }
    }

    public async Task<EuroHeaderResponse?> GetHeaderAsync(int gameCode, string seasonCode, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"Header?gamecode={gameCode}&seasoncode={seasonCode}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<EuroHeaderResponse>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch EuroLeague/EuroCup header for game {GameCode}, season {SeasonCode}", gameCode, seasonCode);
            return null;
        }
    }
}
