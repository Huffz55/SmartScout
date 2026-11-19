using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using SmartScout.Application.Interfaces;
using SmartScout.Infrastructure;
using SmartScout.Infrastructure.Services;
using Xunit;

namespace SmartScout.UnitTests;

public class HttpResilienceRegistrationTests
{
    [Fact]
    public void ConfigureResilienceOptions_ShouldMatchExpectedTimeoutsAndRetryPolicies()
    {
        var bioOptions = new HttpStandardResilienceOptions();
        InfrastructureServiceRegistration.ConfigureBioEnrichmentResilience(bioOptions);

        Assert.Equal(TimeSpan.FromSeconds(12), bioOptions.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(40), bioOptions.TotalRequestTimeout.Timeout);
        Assert.Equal(2, bioOptions.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), bioOptions.Retry.Delay);
        Assert.Equal(DelayBackoffType.Exponential, bioOptions.Retry.BackoffType);
        Assert.True(bioOptions.Retry.UseJitter);
        Assert.Equal(TimeSpan.FromSeconds(45), bioOptions.CircuitBreaker.SamplingDuration);
        Assert.Equal(15, bioOptions.CircuitBreaker.MinimumThroughput);
        Assert.Equal(0.5, bioOptions.CircuitBreaker.FailureRatio);

        var injuryOptions = new HttpStandardResilienceOptions();
        InfrastructureServiceRegistration.ConfigureInjurySyncResilience(injuryOptions);

        Assert.Equal(TimeSpan.FromSeconds(10), injuryOptions.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(35), injuryOptions.TotalRequestTimeout.Timeout);
        Assert.Equal(2, injuryOptions.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), injuryOptions.Retry.Delay);
        Assert.Equal(DelayBackoffType.Exponential, injuryOptions.Retry.BackoffType);
        Assert.True(injuryOptions.Retry.UseJitter);
        Assert.Equal(TimeSpan.FromSeconds(45), injuryOptions.CircuitBreaker.SamplingDuration);
        Assert.Equal(10, injuryOptions.CircuitBreaker.MinimumThroughput);
        Assert.Equal(0.5, injuryOptions.CircuitBreaker.FailureRatio);
    }

    [Theory]
    [InlineData(nameof(IPlayerBioEnrichmentService))]
    [InlineData(nameof(IPlayerInjurySyncService))]
    public async Task RegisteredClients_ShouldSetInfiniteHttpClientTimeout_AndRetryTransient503To200(string clientName)
    {
        int attempts = 0;
        var stubHandler = new DelegateHttpMessageHandler((_, _) =>
        {
            int current = Interlocked.Increment(ref attempts);
            var status = current < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        });

        using var provider = BuildTestProvider(
            stubHandler,
            options =>
            {
                options.Retry.Delay = TimeSpan.FromMilliseconds(5);
                options.Retry.UseJitter = false;
            });

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(clientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);

        using var response = await client.GetAsync("https://stats.smartscout.test/sample");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task SelectPipelineByAuthority_ShouldIsolateCircuitBreakerBetweenHosts()
    {
        var stubHandler = new DelegateHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.Host == "host-a.smartscout.test")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var provider = BuildTestProvider(
            stubHandler,
            options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.FromMilliseconds(5);
                options.Retry.UseJitter = false;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.MinimumThroughput = 2;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(nameof(IPlayerInjurySyncService));

        bool breakerOpenedForHostA = false;
        for (int i = 0; i < 6; i++)
        {
            try
            {
                using var _ = await client.GetAsync("https://host-a.smartscout.test/feed");
            }
            catch (BrokenCircuitException)
            {
                breakerOpenedForHostA = true;
                break;
            }
        }

        Assert.True(breakerOpenedForHostA, "Circuit breaker should open for failing host-a.");

        // Same HttpClient instance calling healthy host-b must not be blocked by host-a's open circuit
        using var hostBResponse = await client.GetAsync("https://host-b.smartscout.test/feed");
        Assert.Equal(HttpStatusCode.OK, hostBResponse.StatusCode);
    }

    private static ServiceProvider BuildTestProvider(
        HttpMessageHandler primaryHandler,
        Action<HttpStandardResilienceOptions> configureTestResilience)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=SmartScoutUnitStub;Username=postgres;Password=123"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(config);

        services.AddHttpClient<IPlayerBioEnrichmentService, WikipediaBioEnrichmentService>()
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);
        services.AddHttpClient<IPlayerInjurySyncService, PlayerInjurySyncService>()
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        services.PostConfigureAll(configureTestResilience);

        return services.BuildServiceProvider();
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request, cancellationToken);
    }
}
