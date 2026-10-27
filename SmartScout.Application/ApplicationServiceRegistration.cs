using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SmartScout.Application.Common.Behaviors;
using SmartScout.Application.Interfaces;
using SmartScout.Application.Services;
using System.Reflection;

namespace SmartScout.Application;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMemoryCache();
        services.AddScoped<IPlayerAnalyticsService, PlayerAnalyticsService>();
        services.AddScoped<ISmartScoutSearchEngine, SmartScoutSearchEngine>();
        services.AddScoped<ISleeperScoutingService, SleeperScoutingService>();
        services.AddValidatorsFromAssembly(assembly);

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });
        
        return services;
    }
}
