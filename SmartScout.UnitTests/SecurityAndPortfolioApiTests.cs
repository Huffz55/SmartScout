using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartScout.Api.Attributes;
using SmartScout.Api.Controllers;
using SmartScout.Application.UseCases.Players;
using SmartScout.Domain.Entities;
using Xunit;

namespace SmartScout.UnitTests;

public class SecurityAndPortfolioApiTests
{
    private static ActionExecutingContext BuildActionContext(IConfiguration config, Action<HttpContext>? configureRequest = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        configureRequest?.Invoke(httpContext);

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    [Fact]
    public async Task ApiKeyAuth_ShouldFailClosedWith503_WhenNoApiKeyIsConfigured()
    {
        string? prevEnv = Environment.GetEnvironmentVariable("SMARTSCOUT_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("SMARTSCOUT_API_KEY", null);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ApiKey"] = ""
            }).Build();

            var attr = new ApiKeyAuthAttribute();
            var context = BuildActionContext(config, ctx =>
            {
                ctx.Request.Headers[ApiKeyAuthAttribute.ApiKeyHeaderName] = "dev-smartscout-secure-key";
            });

            bool nextCalled = false;
            await attr.OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), context.Controller));
            });

            Assert.False(nextCalled);
            var objResult = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, objResult.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMARTSCOUT_API_KEY", prevEnv);
        }
    }

    [Fact]
    public async Task ApiKeyAuth_ShouldReturn401OnInvalidKey_AndPassOnExactMatch()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:ApiKey"] = "unit-test-secret-key"
        }).Build();

        var attr = new ApiKeyAuthAttribute();

        // Wrong key -> 401
        var badContext = BuildActionContext(config, ctx =>
        {
            ctx.Request.Headers[ApiKeyAuthAttribute.ApiKeyHeaderName] = "wrong-key";
        });
        await attr.OnActionExecutionAsync(badContext, () =>
            Task.FromResult(new ActionExecutedContext(badContext, new List<IFilterMetadata>(), badContext.Controller)));
        Assert.IsType<UnauthorizedObjectResult>(badContext.Result);

        // Exact key -> passes through
        var goodContext = BuildActionContext(config, ctx =>
        {
            ctx.Request.Headers[ApiKeyAuthAttribute.ApiKeyHeaderName] = "unit-test-secret-key";
        });
        bool nextCalled = false;
        await attr.OnActionExecutionAsync(goodContext, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(goodContext, new List<IFilterMetadata>(), goodContext.Controller));
        });
        Assert.True(nextCalled);
        Assert.Null(goodContext.Result);
    }

    [Fact]
    public void SelectDiverseDemoTargets_ShouldPickAcrossPositionsAndLeagues()
    {
        var candidates = new[]
        {
            MakePlayerWithStat("Guard One", "PG", "EuroLeague", "SRB", 91m, 1),
            MakePlayerWithStat("Guard Two", "SG", "EuroLeague", "ESP", 89m, 2),
            MakePlayerWithStat("Forward One", "SF", "BSL", "TUR", 88m, 3),
            MakePlayerWithStat("Big One", "C", "ACB", "CRO", 87m, 4),
            MakePlayerWithStat("Wing Two", "PF", "BBL", "GER", 86m, 5),
            MakePlayerWithStat("Guard Three", "PG", "LNB", "FRA", 85m, 6),
            MakePlayerWithStat("Extra Big", "C", "LegaA", "ITA", 84m, 7)
        };

        var selected = SeedDemoWatchlistCommandHandler.SelectDiverseDemoTargets(candidates, maxCount: 6);

        Assert.Equal(6, selected.Count);
        Assert.Equal(6, selected.Select(p => p.Id).Distinct().Count());
        Assert.Contains(selected, p => p.PrimaryPosition == "PG" || p.PrimaryPosition == "SG");
        Assert.Contains(selected, p => p.PrimaryPosition == "SF" || p.PrimaryPosition == "PF");
        Assert.Contains(selected, p => p.PrimaryPosition == "C");
        Assert.True(selected.Select(p => p.League).Distinct().Count() >= 5);
    }

    private static Player MakePlayerWithStat(string fullName, string pos, string league, string nat, decimal eti, int idx)
    {
        var parts = fullName.Split(' ', 2);
        var p = new Player(
            externalApiId: 900000 + idx,
            firstName: parts[0],
            lastName: parts.Length > 1 ? parts[1] : "",
            position: pos,
            height: null,
            weightPounds: null,
            teamName: "Demo Team",
            league: league,
            nationality: nat,
            birthDate: new DateTime(2004, 5, 10, 0, 0, 0, DateTimeKind.Utc));

        var stat = new PlayerSeasonStat(
            playerId: p.Id,
            season: 2027,
            gamesPlayed: 20,
            minutes: 24.0m,
            points: 13.0m,
            assists: 3.5m,
            rebounds: 5.0m,
            steals: 1.2m,
            blocks: 0.8m,
            fgPct: 0.50m,
            fg3Pct: 0.38m,
            ftPct: 0.82m,
            turnovers: 1.5m,
            foulsDrawn: 2.8m,
            league: league,
            teamName: "Demo Team");
        stat.SetTransitionIndex(eti);
        p.AddSeasonStat(stat);
        return p;
    }
}
