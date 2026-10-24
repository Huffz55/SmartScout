using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartScout.Application.Interfaces;
using SmartScout.Infrastructure.Data;
using SmartScout.Infrastructure.Services;
using SmartScout.Infrastructure.Services.EuroLeague;

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

        // Scraper options and dedicated parsing components
        services.Configure<SmartScout.Infrastructure.Options.ScraperOptions>(configuration.GetSection(SmartScout.Infrastructure.Options.ScraperOptions.SectionName));
        services.AddSingleton<SmartScout.Infrastructure.Services.Scraping.BasketballReferenceUrlBuilder>();
        services.AddSingleton<SmartScout.Infrastructure.Services.Scraping.BasketballReferenceDomParser>();

        services.AddHttpClient<EuroLeagueApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://live.euroleague.net/api/");
            client.Timeout = TimeSpan.FromSeconds(25);
        });

        // The scraper uses Playwright internally, registered as regular scoped service
        services.AddScoped<BasketballReferenceScrapingStrategy>();
        services.AddScoped<EuroScrapingSyncStrategy>();
        services.AddScoped<EuroSeasonStatSyncStrategy>();

        // Register Strategies
        services.AddScoped<IPlayerSyncStrategy>(provider => provider.GetRequiredService<BalldontlieSyncStrategy>());
        services.AddScoped<IPlayerSyncStrategy>(provider => provider.GetRequiredService<EuroScrapingSyncStrategy>());
        
        services.AddScoped<ISeasonStatSyncStrategy>(provider => provider.GetRequiredService<BasketballReferenceScrapingStrategy>());
        services.AddScoped<ISeasonStatSyncStrategy>(provider => provider.GetRequiredService<EuroSeasonStatSyncStrategy>());

        // Reporting
        services.AddScoped<IScoutingReportGenerator, SmartScout.Infrastructure.Services.Reporting.QuestPdfScoutingReportGenerator>();

        return services;
    }
}
