using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartScout.Infrastructure.Options;
using SmartScout.Infrastructure.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Api.BackgroundServices;

public class SeasonStatSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SeasonStatSyncBackgroundService> _logger;
    private readonly ScraperOptions _options;

    public SeasonStatSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SeasonStatSyncBackgroundService> logger,
        IOptions<ScraperOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableBackgroundSync && !_options.EnableLiveInjuryAutoSync)
        {
            _logger.LogInformation("Background Sync Service is disabled (EnableBackgroundSync = false, EnableLiveInjuryAutoSync = false).");
            return;
        }

        _logger.LogInformation(
            "Background Sync Service starting (InjuryAutoSync={InjuryAuto}, InjuryInterval={InjuryHrs}h | LiveSeasonAutoSync={StatAuto}, Season={Season}, StatInterval={StatHrs}h).",
            _options.EnableLiveInjuryAutoSync,
            _options.InjurySyncIntervalHours,
            _options.EnableBackgroundSync,
            _options.LiveSeasonYear,
            _options.CurrentSeasonStatRefreshHours);

        int startupDelaySec = Math.Clamp(_options.StartupSyncDelaySeconds, 0, 300);
        if (startupDelaySec > 0)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(startupDelaySec), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        int consecutiveFailureCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextDelay;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<LiveSyncCoordinator>();

                var cycle = await coordinator.RunSyncCycleAsync(DateTime.UtcNow, stoppingToken);
                nextDelay = cycle.RecommendedNextDelay;
                consecutiveFailureCount = 0;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                consecutiveFailureCount++;
                int delayMinutes = Math.Min((int)Math.Pow(2, consecutiveFailureCount - 1), 30);
                nextDelay = TimeSpan.FromMinutes(delayMinutes);

                _logger.LogError(ex, "Unhandled error in Background Sync cycle (Attempt #{Attempt}). Retrying in {Minutes} minutes...",
                    consecutiveFailureCount, delayMinutes);
            }

            try
            {
                await Task.Delay(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
