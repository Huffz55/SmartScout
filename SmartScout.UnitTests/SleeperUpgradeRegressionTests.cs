using System;
using System.Collections.Generic;
using System.Linq;
using SmartScout.Application.Services;
using SmartScout.Domain.Entities;
using SmartScout.Domain.Services;
using Xunit;

namespace SmartScout.UnitTests;

public class SleeperUpgradeRegressionTests
{
    private static void AttachPlayer(PlayerSeasonStat stat, Player player)
    {
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(stat, player);
    }

    [Fact]
    public void StatCalculationService_CorruptedPartialFga_ShouldSanitizeTrueShootingAndEffectiveFg()
    {
        var player = new Player(95001, "Corrupt", "LnbShooter", "G", "6-4", 190, "JDA Dijon", "LNB");
        // Simulates upstream partial-FGA corruption: 14.0 PPG on only 5.0 FGA & 1.8 FTA would yield raw TS% = 1.21 (> 100%)
        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2027,
            gamesPlayed: 18,
            minutes: 25.0m,
            points: 14.0m,
            assists: 3.5m,
            rebounds: 2.8m,
            steals: 1.0m,
            blocks: 0.1m,
            fgPct: 0.480m,
            fg3Pct: 0.410m,
            ftPct: 0.900m,
            turnovers: 1.6m,
            league: "LNB",
            teamName: "JDA Dijon",
            fgm: 2.4m,
            fga: 5.0m,
            fg3m: 2.5m,
            fg3a: 6.1m,
            ftm: 1.6m,
            fta: 1.8m);

        StatCalculationService.EnrichAndCalculate(stat);

        Assert.NotNull(stat.TrueShootingPct);
        Assert.InRange(stat.TrueShootingPct!.Value, 0.300m, 0.850m);
        Assert.NotNull(stat.EffectiveFgPct);
        Assert.InRange(stat.EffectiveFgPct!.Value, 0.000m, 1.000m);
    }

    [Fact]
    public void SleeperScoutingService_MultiSeasonWithTotStint_ShouldDeduplicateGpAndInclude2027()
    {
        var player = new Player(95002, "Combo", "Guard2027", "G", "6-5", 200, "MEM", "NBA",
            birthDate: new DateTime(2002, 4, 15, 0, 0, 0, DateTimeKind.Utc));

        // 2026 has TOT (40 GP) + two split rows (22 GP + 18 GP): must NOT be summed as 80 GP!
        var tot2026 = new PlayerSeasonStat(player.Id, 2026, 40, 15.0m, 8.2m, 2.8m, 2.5m, 0.8m, 0.2m,
            0.460m, 0.380m, 0.820m, 1.1m, trueShootingPct: 0.590m, pir: 11.5m, league: "NBA", teamName: "TOT");
        var splitA = new PlayerSeasonStat(player.Id, 2026, 22, 14.5m, 8.0m, 2.5m, 2.4m, 0.7m, 0.2m,
            0.455m, 0.375m, 0.810m, 1.1m, trueShootingPct: 0.585m, pir: 11.2m, league: "NBA", teamName: "MIA");
        var splitB = new PlayerSeasonStat(player.Id, 2026, 18, 15.6m, 8.4m, 3.1m, 2.6m, 0.9m, 0.2m,
            0.465m, 0.385m, 0.830m, 1.1m, trueShootingPct: 0.595m, pir: 11.8m, league: "NBA", teamName: "MEM");
        var stat2027 = new PlayerSeasonStat(player.Id, 2027, 12, 16.0m, 9.2m, 3.2m, 2.7m, 0.9m, 0.3m,
            0.470m, 0.390m, 0.840m, 1.2m, trueShootingPct: 0.605m, pir: 12.6m, league: "NBA", teamName: "MEM");

        foreach (var s in new[] { tot2026, splitA, splitB, stat2027 })
        {
            StatCalculationService.EnrichAndCalculate(s);
            AttachPlayer(s, player);
        }

        var service = new SleeperScoutingService();
        var results = service.DiscoverSleepers(new[] { tot2026, splitA, splitB, stat2027 }, seasonFilter: 0, top: 25);

        var match = Assert.Single(results);
        Assert.Equal("MultiSeasonConsistency", match.SleeperCategory);
        Assert.Equal(2027, match.Season);
        Assert.Equal(52, match.GamesPlayed);
    }

    [Fact]
    public void SleeperScoutingService_EstablishedNbaStarter_ShouldBeExcludedFromLowMinuteAndMultiSeason()
    {
        var starter = new Player(95003, "Established", "Starter", "F", "6-8", 225, "NYK", "NBA",
            birthDate: new DateTime(1999, 7, 10, 0, 0, 0, DateTimeKind.Utc));

        // 2024-2025 full NBA starter (26 MPG, 14.5 PPG, 65+ GP) -> then drops into a 2026 17-MPG stint (would look like LowMinute without history check)
        var s2024 = new PlayerSeasonStat(starter.Id, 2024, 70, 27.5m, 14.8m, 3.1m, 5.2m, 1.1m, 0.6m,
            0.480m, 0.380m, 0.830m, 1.5m, trueShootingPct: 0.600m, pir: 16.0m, league: "NBA", teamName: "NYK");
        var s2025 = new PlayerSeasonStat(starter.Id, 2025, 68, 25.0m, 13.5m, 2.8m, 4.9m, 1.0m, 0.5m,
            0.475m, 0.375m, 0.820m, 1.4m, trueShootingPct: 0.595m, pir: 14.8m, league: "NBA", teamName: "NYK");
        var s2026 = new PlayerSeasonStat(starter.Id, 2026, 20, 17.5m, 9.8m, 2.2m, 3.8m, 0.8m, 0.4m,
            0.490m, 0.390m, 0.850m, 1.0m, trueShootingPct: 0.615m, pir: 12.0m, league: "NBA", teamName: "NYK");

        foreach (var s in new[] { s2024, s2025, s2026 })
        {
            StatCalculationService.EnrichAndCalculate(s);
            AttachPlayer(s, starter);
        }

        var service = new SleeperScoutingService();
        var lowMinResults = service.DiscoverSleepers(new[] { s2024, s2025, s2026 }, categoryFilter: "LowMinute", seasonFilter: 2026, top: 25);
        var multiResults = service.DiscoverSleepers(new[] { s2024, s2025, s2026 }, seasonFilter: 0, top: 25);

        Assert.Empty(lowMinResults);
        Assert.Empty(multiResults);
    }

    [Fact]
    public void SleeperScoutingService_Ncaad1YoungEliteProspect_ShouldQualifyAsStepUpCandidate()
    {
        var prospect = new Player(95004, "Young", "Freshman", "F", "6-9", 230, "DUKE", "NCAAD1",
            birthDate: new DateTime(2007, 1, 17, 0, 0, 0, DateTimeKind.Utc));

        var ncaaStat = new PlayerSeasonStat(
            playerId: prospect.Id,
            season: 2026,
            gamesPlayed: 34,
            minutes: 33.0m,
            points: 21.5m,
            assists: 4.0m,
            rebounds: 9.8m,
            steals: 1.5m,
            blocks: 1.2m,
            fgPct: 0.560m,
            fg3Pct: 0.390m,
            ftPct: 0.780m,
            turnovers: 2.1m,
            trueShootingPct: 0.650m,
            pir: 29.0m,
            league: "NCAAD1",
            teamName: "DUKE",
            fgm: 7.8m, fga: 13.9m, fg3m: 1.6m, fg3a: 4.1m, ftm: 4.3m, fta: 5.5m);
        StatCalculationService.EnrichAndCalculate(ncaaStat);
        AttachPlayer(ncaaStat, prospect);

        var service = new SleeperScoutingService();
        var results = service.DiscoverSleepers(new[] { ncaaStat }, categoryFilter: "StepUpCandidate", seasonFilter: 2026, top: 25);

        var match = Assert.Single(results);
        Assert.Equal("StepUpCandidate", match.SleeperCategory);
        Assert.Equal("NCAAD1", match.League);
    }

    [Fact]
    public void SmartScoutSearchEngine_TargetSeasonWithHistory_ShouldComputeBuyLowWithoutSaturatingAt999()
    {
        var analytics = new PlayerAnalyticsService();
        var engine = new SmartScoutSearchEngine(analytics);

        var candidate = new Player(95005, "Bounce", "Target", "G", "6-4", 198, "Valencia Basket", "EuroLeague",
            birthDate: new DateTime(2000, 3, 12, 0, 0, 0, DateTimeKind.Utc));
        var peak2024 = new PlayerSeasonStat(candidate.Id, 2024, 24, 29.0m, 18.0m, 5.0m, 3.5m, 1.4m, 0.2m,
            0.48m, 0.40m, 0.86m, 1.8m, trueShootingPct: 0.620m, pir: 19.5m, league: "EuroCup", teamName: "Paris Basketball");
        var dip2026 = new PlayerSeasonStat(candidate.Id, 2026, 20, 14.0m, 7.2m, 2.5m, 1.6m, 0.8m, 0.1m,
            0.47m, 0.39m, 0.85m, 1.0m, trueShootingPct: 0.600m, pir: 8.2m, league: "EuroLeague", teamName: "Valencia Basket");
        StatCalculationService.EnrichAndCalculate(peak2024);
        StatCalculationService.EnrichAndCalculate(dip2026);
        AttachPlayer(peak2024, candidate);
        AttachPlayer(dip2026, candidate);

        // Micro-sample 3 MPG / 4 GP cameo must score materially lower on BuyLowBounceBackIndex than 14 MPG / 20 GP rotation bounce-back
        var microSample = new Player(95006, "Tiny", "Cameo", "G", "6-3", 190, "BSL Team", "BSL",
            birthDate: new DateTime(2003, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var microStat = new PlayerSeasonStat(microSample.Id, 2026, 4, 3.0m, 3.0m, 0.5m, 0.5m, 0.2m, 0.0m,
            0.60m, 0.50m, 0.90m, 0.2m, trueShootingPct: 0.720m, pir: 4.0m, league: "BSL", teamName: "BSL Team");
        StatCalculationService.EnrichAndCalculate(microStat);
        AttachPlayer(microStat, microSample);

        var results = engine.Search("uyuyan dev buy low sleeper euroleague", new[] { peak2024, dip2026 }, top: 10, targetSeason: 2026);

        var topHit = Assert.Single(results);
        Assert.Equal(2026, topHit.Season);
        Assert.True(topHit.BuyLowBounceBackIndex >= 65.0);
        Assert.True(topHit.FitScore < 99.6);

        double bounceIdx = analytics.CalculateBuyLowBounceBackIndex(dip2026, new[] { peak2024, dip2026 });
        double microIdx = analytics.CalculateBuyLowBounceBackIndex(microStat);
        Assert.True(bounceIdx > microIdx + 25.0);
    }

    [Fact]
    public void StatCalculationService_RejectsShotMismatchTrueShootingInflation()
    {
        var player = new Player(95007, "LNB", "Wing", "SF", "6-7", 205, "Nanterre 92", "LNB",
            birthDate: new DateTime(2007, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        // Simulates upstream partial-FGA bug where 2*FGM + 3PM + FTM (7.5) disagrees with Points (8.9) -> naive TS = 0.856
        var corruptedLnb = new PlayerSeasonStat(player.Id, 2026, 29, 18.4m, 8.9m, 2.0m, 3.8m, 0.8m, 0.4m,
            0.534m, 0.405m, 0.847m, 1.1m, league: "LNB", teamName: "Nanterre 92",
            fgm: 2.2m, fga: 4.1m, fg3m: 1.0m, fg3a: 2.6m, ftm: 2.1m, fta: 2.5m);

        StatCalculationService.EnrichAndCalculate(corruptedLnb);

        Assert.NotNull(corruptedLnb.TrueShootingPct);
        Assert.InRange(corruptedLnb.TrueShootingPct!.Value, 0.550m, 0.680m);
    }

    [Fact]
    public void DiscoverMultiSeasonSleepers_RanksYoungShootingWingAboveVeteranNbaBenchCenter()
    {
        var youngWing = new Player(95008, "Euro", "Wing", "SF", "6-7", 205, "Joventut", "ACB",
            birthDate: new DateTime(2004, 5, 10, 0, 0, 0, DateTimeKind.Utc));
        var wing2025 = new PlayerSeasonStat(youngWing.Id, 2025, 28, 17.5m, 9.2m, 2.4m, 3.6m, 1.0m, 0.3m,
            0.46m, 0.385m, 0.83m, 1.1m, trueShootingPct: 0.590m, pir: 12.0m, league: "ACB", teamName: "Joventut",
            fgm: 3.1m, fga: 6.7m, fg3m: 1.6m, fg3a: 4.1m, ftm: 1.4m, fta: 1.7m);
        var wing2026 = new PlayerSeasonStat(youngWing.Id, 2026, 30, 19.5m, 10.4m, 2.8m, 4.2m, 1.1m, 0.4m,
            0.47m, 0.390m, 0.84m, 1.2m, trueShootingPct: 0.600m, pir: 13.5m, league: "ACB", teamName: "Joventut",
            fgm: 3.5m, fga: 7.4m, fg3m: 1.8m, fg3a: 4.6m, ftm: 1.6m, fta: 1.9m);

        var vetCenter = new Player(95009, "Vet", "Center", "C", "6-11", 250, "BOS", "NBA",
            birthDate: new DateTime(1998, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var centerStats = new List<PlayerSeasonStat>();
        for (int s = 2022; s <= 2026; s++)
        {
            var c = new PlayerSeasonStat(vetCenter.Id, s, 40, 13.0m, 6.8m, 0.7m, 3.6m, 0.2m, 0.9m,
                0.61m, 0.25m, 0.72m, 0.7m, trueShootingPct: 0.655m, pir: 9.0m, league: "NBA", teamName: "BOS",
                fgm: 2.7m, fga: 4.4m, fg3m: 0.1m, fg3a: 0.4m, ftm: 1.3m, fta: 1.8m);
            StatCalculationService.EnrichAndCalculate(c);
            AttachPlayer(c, vetCenter);
            centerStats.Add(c);
        }

        foreach (var s in new[] { wing2025, wing2026 })
        {
            StatCalculationService.EnrichAndCalculate(s);
            AttachPlayer(s, youngWing);
        }

        var service = new SleeperScoutingService();
        var allStats = centerStats.Concat(new[] { wing2025, wing2026 }).ToList();
        var results = service.DiscoverSleepers(allStats, seasonFilter: 0, top: 10);

        Assert.Equal(2, results.Count);
        Assert.Equal("Euro Wing", results[0].FullName);
        Assert.True(results[0].SleeperScore > results[1].SleeperScore);
    }
}
