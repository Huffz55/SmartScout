using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartScout.Application.UseCases.SeasonStats;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Api.BackgroundServices;

public class SeasonStatSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SeasonStatSyncBackgroundService> _logger;

    public SeasonStatSyncBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SeasonStatSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Season Stat Sync Background Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                var command = new SyncSeasonStatsCommand();
                var count = await mediator.Send(command, stoppingToken);

                _logger.LogInformation($"Successfully synced stats for {count} records.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while syncing season stats.");
            }

            // Run once every 24 hours
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
