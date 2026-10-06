using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartScout.Application.UseCases.Players;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Api.BackgroundServices;

public class PlayerSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerSyncBackgroundService> _logger;

    public PlayerSyncBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PlayerSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PlayerSyncBackgroundService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting player sync process at: {time}", DateTimeOffset.Now);

                using (var scope = _scopeFactory.CreateScope())
                {
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                    
                    var command = new SyncPlayersCommand { Cursor = 0 }; // Starting point
                    var count = await mediator.Send(command, stoppingToken);

                    _logger.LogInformation("Successfully synced {count} players.", count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during player sync.");
            }

            // Wait 24 hours before running again
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
