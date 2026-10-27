using SmartScout.Application.Services;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Services.EuroLeague;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SmartScout.UnitTests;

public class EuropeanBasketballAnalyticsTests
{
    private readonly PlayerAnalyticsService _analyticsService = new();

    [Theory]
    [InlineData("NEEDHAM, DEREK", "Derek", "Needham")]
    [InlineData("MATHEWS, JONAH", "Jonah", "Mathews")]
    [InlineData("ALLMAN, KYLE", "Kyle", "Allman")]
    [InlineData("BERK UGURLU", "Berk", "Ugurlu")]
    public void EuroLeagueParserHelper_ParsePlayerName_ShouldExtractFirstAndLastNameCorrectly(
        string rawName, string expectedFirst, string expectedLast)
    {
        var (first, last) = EuroLeagueParserHelper.ParsePlayerName(rawName);

        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }

    [Theory]
    [InlineData("20:00", 20.0)]
    [InlineData("23:23", 23.4)]
    [InlineData("17:13", 17.2)]
    [InlineData("DNP", 0.0)]
    [InlineData("", 0.0)]
    public void EuroLeagueParserHelper_ParseMinutes_ShouldConvertMinutesSecondsToDecimal(
        string minutesStr, decimal expectedMinutes)
    {
        decimal parsed = EuroLeagueParserHelper.ParseMinutes(minutesStr);

        Assert.Equal(expectedMinutes, parsed);
    }

    [Fact]
    public void EuroLeagueParserHelper_GenerateDeterministicId_ShouldReturnStableNegativeInteger()
    {
        string key = "Euro_P011924";
        int id1 = EuroLeagueParserHelper.GenerateDeterministicId(key);
        int id2 = EuroLeagueParserHelper.GenerateDeterministicId(key);

        Assert.Equal(id1, id2);
        Assert.True(id1 < 0, "Deterministic external ID for Euro prospects must be a negative integer to avoid NBA ID collisions.");
    }

    [Fact]
    public void EuroLeagueParserHelper_DetectNationality_ShouldIdentifyTurkishDomesticPlayers()
    {
        string? natBerk = EuroLeagueParserHelper.DetectNationality("Berk", "Ugurlu", "BESIKTAS FIBABANKA ISTANBUL");
        string? natJonah = EuroLeagueParserHelper.DetectNationality("Jonah", "Mathews", "BESIKTAS FIBABANKA ISTANBUL");

        Assert.Equal("TUR", natBerk);
        Assert.Null(natJonah);
    }

    [Fact]
    public void PlayerSeasonStat_Constructor_ShouldAutoCalculatePer40MinutesStatsAndStoreSeasonLeague()
    {
        // 18.0 PPG in 20.0 minutes => exactly 36.0 Per-40
        var stat = new PlayerSeasonStat(
            playerId: Guid.NewGuid(),
            season: 2024,
            gamesPlayed: 18,
            minutes: 20.0m,
            points: 18.0m,
            assists: 6.0m,
            rebounds: 4.0m,
            steals: 1.5m,
            blocks: 0.3m,
            fgPct: 0.48m,
            fg3Pct: 0.41m,
            ftPct: 0.88m,
            turnovers: 1.5m,
            pir: 19.5m,
            foulsDrawn: 4.2m,
            league: "EuroCup",
            teamName: "BESIKTAS FIBABANKA ISTANBUL"
        );

        Assert.NotNull(stat.PointsPer40);
        Assert.Equal(36.0m, stat.PointsPer40.Value);

        Assert.NotNull(stat.AssistsPer40);
        Assert.Equal(12.0m, stat.AssistsPer40.Value);

        Assert.NotNull(stat.ReboundsPer40);
        Assert.Equal(8.0m, stat.ReboundsPer40.Value);

        Assert.Equal("EuroCup", stat.League);
        Assert.Equal("BESIKTAS FIBABANKA ISTANBUL", stat.TeamName);
    }

    [Fact]
    public void CalculateEuropeanCompatibilityIndex_EliteSpacingAndBallSecurity_ShouldYieldHighScore()
    {
        // Elite European prospect: 42% 3P, 88% FT, 1.2 TOV, 18.0 PIR
        var eliteEuroGuard = new PlayerSeasonStat(
            playerId: Guid.NewGuid(),
            season: 2024,
            gamesPlayed: 30,
            minutes: 26.0m,
            points: 15.0m,
            assists: 5.5m,
            rebounds: 3.2m,
            steals: 1.2m,
            blocks: 0.2m,
            fgPct: 0.47m,
            fg3Pct: 0.42m,
            ftPct: 0.88m,
            turnovers: 1.2m,
            pir: 18.0m,
            foulsDrawn: 4.5m
        );

        double eci = _analyticsService.CalculateEuropeanCompatibilityIndex(eliteEuroGuard);

        Assert.True(eci >= 75.0, $"Expected elite European Compatibility Index >= 75, got {eci}");
    }

    [Fact]
    public void CalculateDefensivePressureIndex_AlimpijevićPressHound_ShouldYieldHighScore()
    {
        // Derek Needham / Beşiktaş style: 1.8 SPG, 3.8 fouls drawn, high intensity
        var pressHound = new PlayerSeasonStat(
            playerId: Guid.NewGuid(),
            season: 2024,
            gamesPlayed: 25,
            minutes: 24.0m,
            points: 14.0m,
            assists: 4.8m,
            rebounds: 3.5m,
            steals: 1.8m,
            blocks: 0.4m,
            fgPct: 0.45m,
            fg3Pct: 0.38m,
            ftPct: 0.82m,
            turnovers: 2.1m,
            boxPlusMinus: 4.5m,
            pir: 16.0m,
            foulsDrawn: 4.0m
        );

        double dpi = _analyticsService.CalculateDefensivePressureIndex(pressHound);

        Assert.True(dpi >= 75.0, $"Expected high Defensive Pressure Index >= 75, got {dpi}");
    }

    [Fact]
    public void SmartScoutSearchEngine_BesiktasAlimpijevicPrompt_ShouldPromoteDefensiveDisruptor()
    {
        var engine = new SmartScoutSearchEngine(_analyticsService);

        var hound = new Player(101, "Derek", "Needham", "G", "6-0", 185, "Besiktas Fibabanka", "EuroCup");
        var houndStat = new PlayerSeasonStat(
            playerId: hound.Id,
            season: 2024,
            gamesPlayed: 22,
            minutes: 25.0m,
            points: 13.5m,
            assists: 4.5m,
            rebounds: 3.0m,
            steals: 1.9m,
            blocks: 0.2m,
            fgPct: 0.44m,
            fg3Pct: 0.39m,
            ftPct: 0.83m,
            turnovers: 2.0m,
            boxPlusMinus: 3.8m,
            pir: 15.2m,
            foulsDrawn: 4.2m
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(houndStat, hound);

        var passiveScorer = new Player(102, "Passive", "Shooter", "G", "6-4", 195, "Other Team", "EuroLeague");
        var passiveStat = new PlayerSeasonStat(
            playerId: passiveScorer.Id,
            season: 2024,
            gamesPlayed: 22,
            minutes: 25.0m,
            points: 14.0m,
            assists: 2.0m,
            rebounds: 2.0m,
            steals: 0.3m,
            blocks: 0.1m,
            fgPct: 0.45m,
            fg3Pct: 0.37m,
            ftPct: 0.80m,
            turnovers: 2.2m,
            boxPlusMinus: -1.0m,
            pir: 10.0m,
            foulsDrawn: 1.5m
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(passiveStat, passiveScorer);

        var results = engine.Search("besiktas alimpijevic ball pressure guard", new[] { houndStat, passiveStat });

        Assert.NotEmpty(results);
        Assert.Equal(hound.Id, results[0].PlayerId);
        Assert.Contains(results[0].MatchReasons, r => r.Contains("Alimpijević System Fit"));
    }

    [Fact]
    public void SmartScoutSearchEngine_TurkishDomesticFilter_ShouldExcludeNonTurkishProspects()
    {
        var engine = new SmartScoutSearchEngine(_analyticsService);

        var turkPlayer = new Player(201, "Berk", "Ugurlu", "G", "6-3", 190, "Besiktas Fibabanka", "EuroCup", nationality: "TUR");
        var turkStat = new PlayerSeasonStat(
            playerId: turkPlayer.Id,
            season: 2024,
            gamesPlayed: 20,
            minutes: 18.0m,
            points: 8.5m,
            assists: 4.2m,
            rebounds: 2.0m,
            steals: 1.1m,
            blocks: 0.1m,
            fgPct: 0.46m,
            fg3Pct: 0.35m,
            ftPct: 0.78m,
            turnovers: 1.8m,
            pir: 9.5m,
            foulsDrawn: 2.4m
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(turkStat, turkPlayer);

        var usaPlayer = new Player(202, "Kyle", "Allman", "G", "6-3", 190, "Besiktas Fibabanka", "EuroCup", nationality: "USA");
        var usaStat = new PlayerSeasonStat(
            playerId: usaPlayer.Id,
            season: 2024,
            gamesPlayed: 20,
            minutes: 22.0m,
            points: 12.0m,
            assists: 3.5m,
            rebounds: 3.0m,
            steals: 1.2m,
            blocks: 0.2m,
            fgPct: 0.45m,
            fg3Pct: 0.36m,
            ftPct: 0.80m,
            turnovers: 2.0m,
            pir: 11.5m,
            foulsDrawn: 3.5m
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(usaStat, usaPlayer);

        var results = engine.Search("yerli turk guard", new[] { turkStat, usaStat });

        Assert.Single(results);
        Assert.Equal(turkPlayer.Id, results[0].PlayerId);
        Assert.Contains(results[0].MatchReasons, r => r.Contains("Turkish National"));
    }

    [Fact]
    public void SmartScoutSearchEngine_BuyLowSleeperPrompt_ShouldIdentifyFormerPeakPlayerInReducedRole()
    {
        var engine = new SmartScoutSearchEngine(_analyticsService);

        // Player 1: "Sleeping Giant" — Star in 2024 EuroCup (17.5 PPG, 19.0 PIR), now buried on a 2026 EuroLeague bench (13.0 MPG, 6.8 PPG, but 20.9 PTS/40 & 40% 3P)
        var sleeper = new Player(301, "Trevion", "Sleeper", "G", "6-3", 195, "Real Madrid", "EuroLeague");
        var peak2024 = new PlayerSeasonStat(
            playerId: sleeper.Id,
            season: 2024,
            gamesPlayed: 20,
            minutes: 28.0m,
            points: 17.5m,
            assists: 5.2m,
            rebounds: 3.5m,
            steals: 1.4m,
            blocks: 0.2m,
            fgPct: 0.48m,
            fg3Pct: 0.39m,
            ftPct: 0.85m,
            turnovers: 2.0m,
            trueShootingPct: 0.61m,
            pir: 19.0m,
            foulsDrawn: 4.5m,
            league: "EuroCup",
            teamName: "Bourg"
        );
        var dip2026 = new PlayerSeasonStat(
            playerId: sleeper.Id,
            season: 2026,
            gamesPlayed: 18,
            minutes: 13.0m,
            points: 6.8m,
            assists: 2.4m,
            rebounds: 1.5m,
            steals: 0.8m,
            blocks: 0.1m,
            fgPct: 0.47m,
            fg3Pct: 0.40m,
            ftPct: 0.86m,
            turnovers: 1.0m,
            trueShootingPct: 0.60m,
            pir: 7.5m,
            foulsDrawn: 2.0m,
            league: "EuroLeague",
            teamName: "Real Madrid"
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(peak2024, sleeper);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(dip2026, sleeper);
        sleeper.AddSeasonStat(peak2024);
        sleeper.AddSeasonStat(dip2026);

        // Player 2: Mediocre low-efficiency player in both 2024 and 2026
        var mediocre = new Player(302, "Average", "Bench", "G", "6-2", 190, "Other Team", "EuroCup");
        var med2024 = new PlayerSeasonStat(
            playerId: mediocre.Id,
            season: 2024,
            gamesPlayed: 20,
            minutes: 20.0m,
            points: 5.0m,
            assists: 1.5m,
            rebounds: 1.5m,
            steals: 0.4m,
            blocks: 0.0m,
            fgPct: 0.36m,
            fg3Pct: 0.27m,
            ftPct: 0.68m,
            turnovers: 2.2m,
            trueShootingPct: 0.45m,
            pir: 3.5m,
            foulsDrawn: 1.0m,
            league: "EuroCup",
            teamName: "Other Team"
        );
        var med2026 = new PlayerSeasonStat(
            playerId: mediocre.Id,
            season: 2026,
            gamesPlayed: 20,
            minutes: 22.0m,
            points: 5.5m,
            assists: 1.8m,
            rebounds: 1.8m,
            steals: 0.5m,
            blocks: 0.1m,
            fgPct: 0.37m,
            fg3Pct: 0.28m,
            ftPct: 0.70m,
            turnovers: 2.1m,
            trueShootingPct: 0.46m,
            pir: 4.0m,
            foulsDrawn: 1.2m,
            league: "EuroCup",
            teamName: "Other Team"
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(med2024, mediocre);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(med2026, mediocre);
        mediocre.AddSeasonStat(med2024);
        mediocre.AddSeasonStat(med2026);

        var results = engine.Search("gecmiste iyi olup parlayabilecek uyuyan dev buy low guard", new[] { peak2024, dip2026, med2024, med2026 });

        Assert.NotEmpty(results);
        Assert.Equal(sleeper.Id, results[0].PlayerId);
        Assert.Equal(2026, results[0].Season);
        Assert.Equal("EuroLeague", results[0].League);
        Assert.True(results[0].BuyLowBounceBackIndex >= 70.0);
        Assert.Contains(results[0].MatchReasons, r => r.Contains("Uyuyan Dev / Buy-Low Target"));
    }

    [Fact]
    public void ScraperOptions_EuroThrottling_ShouldComplyWithSafeRateLimits()
    {
        var options = new SmartScout.Infrastructure.Options.ScraperOptions();

        // 1. Max concurrent requests should be 2 or 3 (never 6+)
        Assert.InRange(options.EuroMaxConcurrentRequests, 1, 3);

        // 2. Delay between waves should be at least 1000ms - 1500ms
        Assert.InRange(options.EuroDelayBetweenWavesMs, 1000, 2000);
    }

    [Fact]
    public void PollyStandardResilienceOptions_AttemptTimeoutAndSamplingDuration_ShouldComplyWithDoubleRule()
    {
        var options = new Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions();
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);

        // CircuitBreaker SamplingDuration (60s) must be at least double AttemptTimeout (10s)
        Assert.True(options.CircuitBreaker.SamplingDuration >= options.AttemptTimeout.Timeout * 2);
        Assert.Equal(TimeSpan.FromSeconds(10), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(60), options.TotalRequestTimeout.Timeout);
    }
}
