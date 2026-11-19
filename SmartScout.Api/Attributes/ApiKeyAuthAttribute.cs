using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SmartScout.Api.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAuthAttribute : Attribute, IAsyncActionFilter
{
    public const string ApiKeyHeaderName = "X-Api-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        string? configuredApiKey = ResolveConfiguredApiKey(configuration);

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            context.Result = new ObjectResult(new
            {
                Message = "Sync API authentication key is not configured on this server. Set Authentication:ApiKey or SMARTSCOUT_API_KEY."
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var potentialApiKey) || string.IsNullOrWhiteSpace(potentialApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                Message = $"API Key is required. Please provide '{ApiKeyHeaderName}' in request headers."
            });
            return;
        }

        byte[] providedBytes = Encoding.UTF8.GetBytes(potentialApiKey.ToString());
        byte[] configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);

        // Constant-time comparison to prevent timing attack side-channels
        if (providedBytes.Length != configuredBytes.Length || !CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes))
        {
            context.Result = new UnauthorizedObjectResult(new { Message = "Unauthorized: Invalid API Key provided." });
            return;
        }

        await next();
    }

    internal static string? ResolveConfiguredApiKey(IConfiguration configuration)
    {
        var apiKey = configuration.GetValue<string>("Authentication:ApiKey");
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        apiKey = configuration.GetValue<string>("SMARTSCOUT_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        return Environment.GetEnvironmentVariable("SMARTSCOUT_API_KEY");
    }
}
