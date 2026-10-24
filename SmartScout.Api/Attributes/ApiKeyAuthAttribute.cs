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
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var potentialApiKey) || string.IsNullOrWhiteSpace(potentialApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new { Message = $"API Key is required. Please provide '{ApiKeyHeaderName}' in request headers." });
            return;
        }

        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configuredApiKey = configuration.GetValue<string>("Authentication:ApiKey");

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            configuredApiKey = "dev-smartscout-secure-key";
        }

        byte[] providedBytes = Encoding.UTF8.GetBytes(potentialApiKey!);
        byte[] configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);

        // Constant-time comparison to prevent timing attack side-channels
        if (providedBytes.Length != configuredBytes.Length || !CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes))
        {
            context.Result = new UnauthorizedObjectResult(new { Message = "Unauthorized: Invalid API Key provided." });
            return;
        }

        await next();
    }
}
