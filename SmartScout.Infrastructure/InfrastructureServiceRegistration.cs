using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartScout.Application.Interfaces;
using SmartScout.Infrastructure.Data;
using SmartScout.Infrastructure.Services;

namespace SmartScout.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SmartScoutDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<ISmartScoutDbContext>(provider => provider.GetRequiredService<SmartScoutDbContext>());

        services.AddHttpClient<BalldontlieSyncStrategy>(client =>
        {
            client.BaseAddress = new Uri("https://api.balldontlie.io/v1/players");
            
            var apiKey = configuration["BalldontlieApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("Authorization", apiKey);
            }
        })
        .AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(15);
            options.Retry.MaxRetryAttempts = 10;
        });

        services.AddHttpClient<BasketballReferenceScrapingStrategy>()
        .AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(15);
            options.Retry.MaxRetryAttempts = 10;
        });

        // Register Strategies
        services.AddScoped<IPlayerSyncStrategy>(provider => provider.GetRequiredService<BalldontlieSyncStrategy>());
        services.AddScoped<IPlayerSyncStrategy, EuroScrapingSyncStrategy>();
        
        services.AddScoped<ISeasonStatSyncStrategy>(provider => provider.GetRequiredService<BasketballReferenceScrapingStrategy>());

        return services;
    }
}
