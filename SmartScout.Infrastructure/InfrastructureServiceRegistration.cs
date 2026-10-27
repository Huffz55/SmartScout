using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using SmartScout.Application.Interfaces;
using SmartScout.Infrastructure.Data;
using SmartScout.Infrastructure.Services;
using SmartScout.Infrastructure.Services.EuroLeague;

namespace SmartScout.Infrastructure;

public static class InfrastructureServiceRegistration
{
    static InfrastructureServiceRegistration()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

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
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            options.Retry.MaxRetryAttempts = 5;
        });

        // Scraper options and dedicated parsing components
        services.Configure<SmartScout.Infrastructure.Options.ScraperOptions>(configuration.GetSection(SmartScout.Infrastructure.Options.ScraperOptions.SectionName));
        services.AddSingleton<SmartScout.Infrastructure.Services.Scraping.BasketballReferenceUrlBuilder>();
        services.AddSingleton<SmartScout.Infrastructure.Services.Scraping.BasketballReferenceDomParser>();

        // EuroLeague Live JSON API Client with Polly v8 StandardResilienceHandler (Exponential Backoff + HTTP 429/5xx Retry)
        services.AddHttpClient<EuroLeagueApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://live.euroleague.net/api/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        })
        .AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            options.Retry.MaxRetryAttempts = 5;
            options.Retry.Delay = TimeSpan.FromSeconds(2); // Base backoff: 2s, 4-5s, 8-10s exponential backoff with jitter
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
        });

        // Playwright & API strategies
        services.AddScoped<BasketballReferenceScrapingStrategy>();
        services.AddScoped<EuroScrapingSyncStrategy>();
        services.AddScoped<EuroSeasonStatSyncStrategy>();
        services.AddScoped<GlobalLeagueTableScrapingStrategy>();

        // Register Player Sync Strategies (NBA + EuroLeague/EuroCup + 12 International/Domestic Leagues)
        services.AddScoped<IPlayerSyncStrategy>(provider => provider.GetRequiredService<BalldontlieSyncStrategy>());
        services.AddScoped<IPlayerSyncStrategy>(provider => provider.GetRequiredService<EuroScrapingSyncStrategy>());
        services.AddScoped<IPlayerSyncStrategy>(provider => provider.GetRequiredService<GlobalLeagueTableScrapingStrategy>());
        
        // Register Season Stat Sync Strategies (NBA + EuroLeague/EuroCup + 12 International/Domestic Leagues)
        services.AddScoped<ISeasonStatSyncStrategy>(provider => provider.GetRequiredService<BasketballReferenceScrapingStrategy>());
        services.AddScoped<ISeasonStatSyncStrategy>(provider => provider.GetRequiredService<EuroSeasonStatSyncStrategy>());
        services.AddScoped<ISeasonStatSyncStrategy>(provider => provider.GetRequiredService<GlobalLeagueTableScrapingStrategy>());

        // Reporting
        services.AddScoped<IScoutingReportGenerator, SmartScout.Infrastructure.Services.Reporting.QuestPdfScoutingReportGenerator>();

        return services;
    }
}
