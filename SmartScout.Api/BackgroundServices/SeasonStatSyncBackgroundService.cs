using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartScout.Application.UseCases.SeasonStats;
using SmartScout.Infrastructure.Options;
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
        if (!_options.EnableBackgroundSync)
        {
            _logger.LogInformation("Season Stat Sync Background Service is disabled (ScraperSettings:EnableBackgroundSync = false).");
            return;
        }

        _logger.LogInformation("Season Stat Sync Background Service is starting.");

        int consecutiveFailureCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextDelay;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                var command = new SyncSeasonStatsCommand();
                var count = await mediator.Send(command, stoppingToken);

                _logger.LogInformation("Successfully synced stats for {Count} records.", count);

                consecutiveFailureCount = 0;
                nextDelay = TimeSpan.FromHours(24);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                consecutiveFailureCount++;
                // Exponential backoff: 1m, 2m, 4m, 8m... up to 30 minutes
                int delayMinutes = Math.Min((int)Math.Pow(2, consecutiveFailureCount - 1), 30);
                nextDelay = TimeSpan.FromMinutes(delayMinutes);

                _logger.LogError(ex, "Error occurred during season stat sync (Attempt #{Attempt}). Retrying in {Minutes} minutes...", 
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
