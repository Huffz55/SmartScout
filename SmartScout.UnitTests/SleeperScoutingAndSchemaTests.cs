using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Application.Services;
using SmartScout.Application.UseCases.SeasonStats;
using SmartScout.Domain.Entities;
using SmartScout.Domain.Services;
using SmartScout.Infrastructure.Data;
using SmartScout.Infrastructure.Options;
using SmartScout.Infrastructure.Services;
using SmartScout.Infrastructure.Services.EuroLeague;
using SmartScout.Infrastructure.Services.Scraping;
using Xunit;

namespace SmartScout.UnitTests;

public class SleeperScoutingAndSchemaTests
{
    private readonly SleeperScoutingService _sleeperService = new();
    private readonly BasketballReferenceDomParser _domParser = new();

    [Theory]
    [InlineData("Alperen", "Şengün", "alperen sengun")]
    [InlineData("Luka", "Dončić", "luka doncic")]
    [InlineData("Dušan", "Alimpijević", "dusan alimpijevic")]
    [InlineData("İsmail", "Şanlı", "ismail sanli")]
    [InlineData("Willy", "Hernangómez", "willy hernangomez")]
    public void Player_NormalizeName_ShouldStripTurkishAndEuropeanDiacritics(
        string firstName, string lastName, string expectedNormalized)
    {
        var player = new Player(
            externalApiId: 1001,
            firstName: firstName,
            lastName: lastName,
            position: "C-PF",
            height: "6-11",
            weightPounds: 243,
            teamName: "Houston Rockets",
            league: "NBA");

        Assert.Equal(expectedNormalized, player.NormalizedName);
        Assert.Equal($"{firstName} {lastName}", player.FullName);
        Assert.Equal("C", player.PrimaryPosition);
        Assert.Equal("PF", player.SecondaryPosition);
        Assert.Equal(211, player.HeightCm);
        Assert.Equal(110, player.WeightKg);
    }

    [Fact]
    public void StatCalculationService_ShouldComputeExactPer36Per40AndAdvancedMetrics()
    {
        var stat = new PlayerSeasonStat(
            playerId: Guid.NewGuid(),
            season: 2025,
            gamesPlayed: 25,
            minutes: 18.0m,
            points: 12.0m,
            assists: 4.5m,
            rebounds: 6.0m,
            steals: 1.5m,
            blocks: 0.9m,
            fgPct: 0.500m,
            fg3Pct: 0.400m,
            ftPct: 0.850m,
            turnovers: 1.5m,
            foulsDrawn: 3.2m,
            league: "EuroCup",
            teamName: "BESIKTAS FIBABANKA ISTANBUL",
            fgm: 4.0m,
            fga: 8.0m,
            fg3m: 2.0m,
            fg3a: 5.0m,
            fg2m: 2.0m,
            fg2a: 3.0m,
            ftm: 2.0m,
            fta: 2.35m,
            orb: 1.8m,
            drb: 4.2m,
            pfCommitted: 2.0m
        );

        // 18.0 MPG => Per-36 multiplier is 2.0x
        Assert.Equal(24.0m, stat.PtsPer36);
        Assert.Equal(12.0m, stat.RebPer36);
        Assert.Equal(9.0m, stat.AstPer36);
        Assert.Equal(3.0m, stat.StlPer36);
        Assert.Equal(1.8m, stat.BlkPer36);
        Assert.Equal(3.0m, stat.TovPer36);

        // Per-40: 12.0 / 18.0 * 40 = 26.7
        Assert.Equal(26.7m, stat.PointsPer40);
        Assert.Equal(10.0m, stat.AssistsPer40);
        Assert.Equal(13.3m, stat.ReboundsPer40);

        // eFG% = (FGM + 0.5 * 3PM) / FGA = (4.0 + 1.0) / 8.0 = 0.625
        Assert.Equal(0.625m, stat.EffectiveFgPct);

        // AST/TOV = 4.5 / 1.5 = 3.00
        Assert.Equal(3.00m, stat.AssistToTurnoverRatio);

        // TS% = 12 / (2 * (8 + 0.44 * 2.35)) = 12 / 18.068 = 0.664
        Assert.Equal(0.664m, stat.TrueShootingPct);

        // EuroCup DifficultyCoefficient = 0.78
        Assert.Equal(0.78m, stat.LeagueDifficultyCoefficient);

        // GameScoreAverage, PIR, UsagePct should all be populated
        Assert.NotNull(stat.GameScoreAverage);
        Assert.True(stat.GameScoreAverage > 10.0m);
        Assert.NotNull(stat.Pir);
        Assert.NotNull(stat.UsagePct);
    }

    [Fact]
    public void EuroPlayerStatAccumulator_ShouldPopulateFullBoxScoreAndRawJsonbPayload()
    {
        Guid playerId = Guid.NewGuid();
        var acc = new EuroPlayerStatAccumulator(playerId, "EuroLeague", "VALENCIA BASKET");

        acc.Accumulate(new EuroPlayerGameStats
        {
            Player_ID = "P001",
            Player = "MARI, LUCAS",
            IsStarter = 1,
            Minutes = "20:00",
            Points = 16,
            FieldGoalsMade2 = 3,
            FieldGoalsAttempted2 = 5,
            FieldGoalsMade3 = 2,
            FieldGoalsAttempted3 = 4,
            FreeThrowsMade = 4,
            FreeThrowsAttempted = 4,
            OffensiveRebounds = 2,
            DefensiveRebounds = 4,
            TotalRebounds = 6,
            Assistances = 5,
            Steals = 2,
            BlocksFavour = 1,
            Turnovers = 1,
            FoulsCommited = 2,
            FoulsReceived = 5,
            Valuation = 25,
            Plusminus = 12
        });

        var stat = acc.ToPlayerSeasonStat(2026);

        Assert.NotNull(stat);
        Assert.Equal("EuroLeague", stat!.League);
        Assert.Equal("VALENCIA BASKET", stat.TeamName);
        Assert.Equal(0.88m, stat.LeagueDifficultyCoefficient);
        Assert.Equal(1, stat.GamesStarted);
        Assert.Equal(28.8m, stat.PtsPer36);
        Assert.Equal(32.0m, stat.PointsPer40);
        Assert.NotNull(stat.RawScrapedPayload);
        Assert.Contains("EuroLeagueLiveApi", stat.RawScrapedPayload);
    }

    [Fact]
    public void SleeperScoutingService_ShouldSurfaceAllThreeSleeperArchetypes()
    {
        // 1. Forgotten Peak Candidate (Age 26: Star in 2024, reduced role in 2026)
        var peakPlayer = new Player(501, "Markus", "HowardClone", "G", "5-11", 175, "Baskonia", "EuroLeague", birthDate: new DateTime(1999, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var s2024 = new PlayerSeasonStat(
            playerId: peakPlayer.Id,
            season: 2024,
            gamesPlayed: 30,
            minutes: 26.0m,
            points: 18.5m,
            assists: 3.5m,
            rebounds: 2.2m,
            steals: 1.1m,
            blocks: 0.1m,
            fgPct: 0.46m,
            fg3Pct: 0.40m,
            ftPct: 0.88m,
            turnovers: 2.0m,
            trueShootingPct: 0.61m,
            pir: 16.5m,
            league: "EuroLeague",
            teamName: "Baskonia",
            fgm: 6.2m, fga: 13.5m, fg3m: 3.4m, fg3a: 8.5m, ftm: 2.7m, fta: 3.1m);
        var s2026 = new PlayerSeasonStat(
            playerId: peakPlayer.Id,
            season: 2026,
            gamesPlayed: 16,
            minutes: 13.5m,
            points: 7.2m,
            assists: 1.8m,
            rebounds: 1.2m,
            steals: 0.6m,
            blocks: 0.0m,
            fgPct: 0.44m,
            fg3Pct: 0.38m,
            ftPct: 0.85m,
            turnovers: 1.0m,
            trueShootingPct: 0.58m,
            pir: 6.8m,
            league: "EuroLeague",
            teamName: "Baskonia",
            fgm: 2.5m, fga: 5.7m, fg3m: 1.4m, fg3a: 3.7m, ftm: 0.8m, fta: 0.9m);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(s2024, peakPlayer);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(s2026, peakPlayer);

        // 2. Low-Minute Monster (Playing 14.0 MPG, posting 23.1 PTS/36, 64% TS%, 2.5 AST/TOV)
        var monsterPlayer = new Player(502, "Tarik", "BiberovicClone", "F", "6-7", 218, "Fenerbahce Beko", "EuroLeague", birthDate: new DateTime(2001, 1, 28, 0, 0, 0, DateTimeKind.Utc));
        var monsterStat = new PlayerSeasonStat(
            playerId: monsterPlayer.Id,
            season: 2026,
            gamesPlayed: 22,
            minutes: 14.0m,
            points: 9.0m,
            assists: 2.0m,
            rebounds: 3.5m,
            steals: 0.8m,
            blocks: 0.3m,
            fgPct: 0.52m,
            fg3Pct: 0.45m,
            ftPct: 0.86m,
            turnovers: 0.8m,
            league: "EuroLeague",
            teamName: "Fenerbahce Beko",
            fgm: 3.2m, fga: 6.1m, fg3m: 1.8m, fg3a: 4.0m, ftm: 0.8m, fta: 0.9m);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(monsterStat, monsterPlayer);

        // 3. Euro/Domestic Ready to Step Up (Dominating EuroCup Coeff 0.78 with 39% 3P on 5.5 3PA/G & 18.0 PIR)
        var stepUpPlayer = new Player(503, "Jonah", "MathewsClone", "G", "6-3", 205, "Besiktas Fibabanka", "EuroCup", birthDate: new DateTime(1998, 2, 10, 0, 0, 0, DateTimeKind.Utc));
        var stepUpStat = new PlayerSeasonStat(
            playerId: stepUpPlayer.Id,
            season: 2026,
            gamesPlayed: 18,
            minutes: 27.0m,
            points: 16.5m,
            assists: 4.2m,
            rebounds: 3.0m,
            steals: 1.5m,
            blocks: 0.3m,
            fgPct: 0.47m,
            fg3Pct: 0.39m,
            ftPct: 0.84m,
            turnovers: 1.8m,
            pir: 18.0m,
            league: "EuroCup",
            teamName: "Besiktas Fibabanka",
            fgm: 5.8m, fga: 12.3m, fg3m: 2.2m, fg3a: 5.6m, ftm: 2.7m, fta: 3.2m);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(stepUpStat, stepUpPlayer);

        var allStats = new[] { s2024, s2026, monsterStat, stepUpStat };
        var sleepers = _sleeperService.DiscoverSleepers(allStats);

        Assert.Contains(sleepers, s => s.PlayerId == peakPlayer.Id && s.SleeperCategory == "ForgottenPeak");
        Assert.Contains(sleepers, s => s.PlayerId == monsterPlayer.Id && s.SleeperCategory == "LowMinuteMonster");
        Assert.Contains(sleepers, s => s.PlayerId == stepUpPlayer.Id && s.SleeperCategory == "StepUpCandidate");
    }

    [Fact]
    public void SleeperScoutingService_ShouldExcludeEstablishedNbaStars_AndProperlyFormatTwoWayProspects()
    {
        // 1. Established NBA Star (Zion Williamson: 31.5 MPG, 22.9 PPG) -> MUST NOT be in sleepers
        var zion = new Player(987867, "Zion", "Williamson", "PF", "6-6", 284, "New Orleans Pelicans", "NBA");
        var zion2021 = new PlayerSeasonStat(zion.Id, 2021, 61, 33.2m, 27.0m, 3.7m, 7.2m, 0.9m, 0.6m, 0.611m, 0.294m, 0.698m, 2.7m, trueShootingPct: 0.649m, pir: 27.1m, league: "NBA", teamName: "NOP");
        var zion2024 = new PlayerSeasonStat(zion.Id, 2024, 70, 31.5m, 22.9m, 5.0m, 5.8m, 1.1m, 0.7m, 0.570m, 0.333m, 0.702m, 2.8m, trueShootingPct: 0.610m, pir: 22.8m, league: "NBA", teamName: "NOP");
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(zion2021, zion);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(zion2024, zion);

        // 2. Two-Way G-League Sleeper (JD Davison: 16 GP, 34.2 MPG, 22.7 PPG in GLeague + 8 GP in NBA with Boston)
        var jd = new Player(1179998, "JD", "Davison", "PG", "6-1", 195, "BOS", "NBA");
        var jdNba = new PlayerSeasonStat(jd.Id, 2024, 8, 4.9m, 2.0m, 1.3m, 1.3m, 0.1m, 0.1m, 0.417m, 0.429m, 0.750m, 0.5m, league: "NBA", teamName: "BOS");
        var jdGleague = new PlayerSeasonStat(jd.Id, 2024, 16, 34.2m, 22.7m, 8.6m, 5.3m, 1.4m, 0.6m, 0.470m, 0.380m, 0.720m, 3.1m, trueShootingPct: 0.600m, pir: 23.5m, league: "GLeague", teamName: "MNE", fg3a: 5.0m);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(jdNba, jd);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(jdGleague, jd);

        var list = new List<PlayerSeasonStat> { zion2021, zion2024, jdNba, jdGleague };
        var sleepers = _sleeperService.DiscoverSleepers(list, seasonFilter: 2024, top: 10);

        // Assert: Zion must be completely excluded
        Assert.DoesNotContain(sleepers, s => s.FullName.Contains("Zion", StringComparison.OrdinalIgnoreCase));

        // Assert: JD Davison should be detected with Two-Way context and expanded team name
        var jdSleeper = Assert.Single(sleepers, s => s.FullName.Contains("Davison", StringComparison.OrdinalIgnoreCase));
        Assert.True(jdSleeper.IsTwoWayPlayer);
        Assert.Contains("Boston Celtics", jdSleeper.TeamName);
        Assert.Contains("Maine Celtics", jdSleeper.TeamName);
    }

    [Fact]
    public void SleeperScoutingService_MultiSeasonAggregate_ShouldCalculateWeightedAveragesAndConsistencyBonus()
    {
        // Player with 3 modern seasons of steady high-efficiency bench production
        var p = new Player(88001, "Sandro", "MamukelashviliClone", "C", "6-10", 240, "SAS", "NBA", birthDate: new DateTime(1999, 5, 23, 0, 0, 0, DateTimeKind.Utc));
        var s2023 = new PlayerSeasonStat(p.Id, 2023, 24, 18.0m, 9.5m, 2.2m, 6.0m, 0.6m, 0.4m, 0.490m, 0.350m, 0.720m, 1.2m, trueShootingPct: 0.580m, pir: 13.5m, league: "NBA", teamName: "SAS");
        var s2024 = new PlayerSeasonStat(p.Id, 2024, 46, 13.0m, 6.1m, 1.5m, 4.2m, 0.4m, 0.3m, 0.470m, 0.330m, 0.780m, 0.8m, trueShootingPct: 0.575m, pir: 11.2m, league: "NBA", teamName: "SAS");
        var s2025 = new PlayerSeasonStat(p.Id, 2025, 40, 14.5m, 7.8m, 1.8m, 5.0m, 0.5m, 0.4m, 0.505m, 0.360m, 0.750m, 0.9m, trueShootingPct: 0.605m, pir: 14.0m, league: "NBA", teamName: "SAS");
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(s2023, p);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(s2024, p);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(s2025, p);

        var list = new List<PlayerSeasonStat> { s2023, s2024, s2025 };
        // seasonFilter = 0 triggers Multi-Season Career Aggregate
        var multiSeasonSleepers = _sleeperService.DiscoverSleepers(list, seasonFilter: 0, top: 10);

        Assert.NotEmpty(multiSeasonSleepers);
        var res = Assert.Single(multiSeasonSleepers, s => s.PlayerId == p.Id);
        Assert.Equal("MultiSeasonConsistency", res.SleeperCategory);
        Assert.Equal(110, res.GamesPlayed); // 24 + 46 + 40 = 110 games total sample
        Assert.Contains("Stability", res.PeakSeasonContext);
        Assert.True(res.TrueShootingPct > 0.57m);
    }

    [Fact]
    public void SleeperScoutingService_MultiSeason_ShouldExcludeInactiveVeterans_AndTinySampleProspects()
    {
        // 1. Jimmer Fredette: Played only in 2020 (18 GP), 37 years old today -> MUST BE EXCLUDED from modern multi-season sleepers
        var jimmer = new Player(7701, "Jimmer", "Fredette", "G", "6-2", 195, "Panathinaikos OPAP", "GBL");
        var jimmerStat = new PlayerSeasonStat(jimmer.Id, 2020, 18, 19.0m, 13.0m, 3.3m, 1.8m, 0.8m, 0.2m, 0.520m, 0.410m, 0.950m, 1.2m, trueShootingPct: 0.821m, pir: 16.0m, league: "GBL", teamName: "Panathinaikos OPAP");
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(jimmerStat, jimmer);

        // 2. Tiny sample player (< 35 total GP): Played only 18 total GP across 2025/2026 -> MUST BE EXCLUDED from multi-season consistency
        var tinyPlayer = new Player(7702, "N'Faly", "Dante", "C", "6-11", 265, "Houston Rockets", "NBA", birthDate: new DateTime(2001, 10, 19, 0, 0, 0, DateTimeKind.Utc));
        var tinyStat1 = new PlayerSeasonStat(tinyPlayer.Id, 2025, 11, 20.5m, 10.5m, 1.0m, 9.0m, 0.5m, 1.5m, 0.700m, 0.0m, 0.650m, 1.0m, trueShootingPct: 0.750m, pir: 19.0m, league: "GLeague", teamName: "RGV");
        var tinyStat2 = new PlayerSeasonStat(tinyPlayer.Id, 2026, 4, 12.0m, 5.0m, 0.5m, 4.0m, 0.2m, 0.8m, 0.650m, 0.0m, 0.600m, 0.5m, trueShootingPct: 0.700m, pir: 12.0m, league: "NBA", teamName: "HOU");
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(tinyStat1, tinyPlayer);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(tinyStat2, tinyPlayer);

        // 3. Valid modern rotation sleeper (Luka Garza: 4 seasons, 140 GP, active 2025, age 27)
        var garza = new Player(7703, "Luka", "Garza", "C", "6-10", 243, "Minnesota Timberwolves", "NBA", birthDate: new DateTime(1998, 12, 27, 0, 0, 0, DateTimeKind.Utc));
        var garza2024 = new PlayerSeasonStat(garza.Id, 2024, 25, 10.2m, 7.2m, 0.6m, 2.5m, 0.2m, 0.1m, 0.480m, 0.280m, 0.720m, 0.5m, trueShootingPct: 0.590m, pir: 8.5m, league: "NBA", teamName: "MIN");
        var garza2025 = new PlayerSeasonStat(garza.Id, 2025, 45, 12.0m, 8.5m, 0.8m, 3.2m, 0.3m, 0.2m, 0.510m, 0.310m, 0.760m, 0.6m, trueShootingPct: 0.610m, pir: 9.8m, league: "NBA", teamName: "MIN");
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(garza2024, garza);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(garza2025, garza);

        var list = new List<PlayerSeasonStat> { jimmerStat, tinyStat1, tinyStat2, garza2024, garza2025 };
        var sleepers = _sleeperService.DiscoverSleepers(list, seasonFilter: 0, top: 10);

        // Assert: Jimmer Fredette (maxSeason=2020, 1 season) and tiny sample player (15 total GP) are NOT returned
        Assert.DoesNotContain(sleepers, s => s.FullName.Contains("Jimmer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sleepers, s => s.FullName.Contains("Dante", StringComparison.OrdinalIgnoreCase));

        // Assert: Luka Garza is discovered as a valid modern multi-season sleeper
        var garzaResult = Assert.Single(sleepers, s => s.FullName.Contains("Garza", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("MultiSeasonConsistency", garzaResult.SleeperCategory);
        Assert.Equal(70, garzaResult.GamesPlayed);
    }

    [Fact]
    public void Player_CurrentAge_ShouldDynamicallyComputeFromBirthDate_AndU22FilterMustExcludeVeterans()
    {
        // 1. Veteran Milos Teodosic (born 1987-03-19) -> Age ~39
        var teodosic = new Player(1099, "Milos", "Teodosic", "G", "6-5", 196, "Crvena zvezda", "ABA", birthDate: new DateTime(1987, 3, 19, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(teodosic.CurrentAge);
        Assert.True(teodosic.CurrentAge >= 38);

        // 2. Young Core Prospect (born 2004-05-15) -> Age ~21-22
        var youngGun = new Player(1100, "Nikola", "TopicClone", "PG", "6-6", 200, "Mega MIS", "ABA", birthDate: new DateTime(2004, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(youngGun.CurrentAge);
        Assert.True(youngGun.CurrentAge <= 22);

        // 3. Player with missing BirthDate
        var unknownPlayer = new Player(1101, "Ghost", "Prospect", "F", "6-7", 210, "Partizan", "ABA", birthDate: null);
        Assert.Null(unknownPlayer.CurrentAge);

        // Age filter defensive logic:
        int maxAgeCap = 22;
        var players = new List<Player> { teodosic, youngGun, unknownPlayer };
        var u22Qualified = players.Where(p => p.CurrentAge.HasValue && p.CurrentAge.Value <= maxAgeCap).ToList();

        Assert.Single(u22Qualified);
        Assert.Equal("Nikola TopicClone", u22Qualified.First().FullName);
        Assert.DoesNotContain(u22Qualified, p => p.FullName.Contains("Teodosic"));
        Assert.DoesNotContain(u22Qualified, p => p.FullName.Contains("Ghost"));
    }

    [Fact]
    public void GlobalLeagueEndpointCatalog_ShouldMapAll15SeededLeaguesWithValidUrls()
    {
        var seededLeagues = BasketballLeague.GetDefaultSeedLeagues();
        var catalogLeagues = GlobalLeagueEndpointCatalog.GetAll();

        Assert.Equal(15, seededLeagues.Count);
        Assert.Equal(15, catalogLeagues.Count);

        foreach (var seed in seededLeagues)
        {
            var def = GlobalLeagueEndpointCatalog.GetByCode(seed.Code);
            Assert.NotNull(def);
            string url2025 = def!.BuildPrimaryUrl(2025);
            Assert.StartsWith("https://", url2025);
            Assert.Contains("2025", url2025);
        }
    }

    [Theory]
    [InlineData("2024-25", 2025)]
    [InlineData("2024-2025", 2025)]
    [InlineData("2024/25", 2025)]
    [InlineData("2025", 2025)]
    [InlineData("2018-19", 2019)]
    public void BasketballSeason_ParseSeasonEndYear_ShouldSupportMultiFormatSeasonStrings(
        string input, int expectedEndYear)
    {
        int? parsed = BasketballSeason.ParseSeasonEndYear(input);
        Assert.Equal(expectedEndYear, parsed);
    }

    [Fact]
    public void SyncFilterContext_ShouldSkipCompletedCheckpointsUnlessForceIsTrue()
    {
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SyncFilterContext.BuildJobKey("EuroCup", 2024),
            SyncFilterContext.BuildJobKey("BSL", 2025)
        };

        var normalFilter = new SyncFilterContext
        {
            LeagueCode = "EuroCup",
            Season = null,
            Force = false,
            CompletedJobKeys = completed
        };

        // 2024 EuroCup is completed -> skipped
        Assert.False(normalFilter.ShouldProcess("EuroCup", 2024));
        // 2025 EuroCup is not completed -> processed
        Assert.True(normalFilter.ShouldProcess("EuroCup", 2025));
        // EuroLeague doesn't match LeagueCode="EuroCup" -> skipped
        Assert.False(normalFilter.ShouldProcess("EuroLeague", 2025));

        var forcedFilter = new SyncFilterContext
        {
            LeagueCode = "EuroCup",
            Season = 2024,
            Force = true,
            CompletedJobKeys = completed
        };

        // Force=true overrides completed checkpoint
        Assert.True(forcedFilter.ShouldProcess("EuroCup", 2024));
    }

    [Fact]
    public void BasketballReferenceDomParser_ParseLeagueSeasonTable_ShouldParseBRefAndRealGmTables()
    {
        string bRefHtml = @"
        <html><body>
            <table id='per_game_stats' class='stats_table'>
                <tbody>
                    <tr>
                        <td data-stat='player' data-append-csv='ugurlbe01'>Berk Uğurlu</td>
                        <td data-stat='pos'>G</td>
                        <td data-stat='team'>Beşiktaş Fibabanka</td>
                        <td data-stat='g'>28</td>
                        <td data-stat='mp_per_g'>24.5</td>
                        <td data-stat='pts_per_g'>10.2</td>
                        <td data-stat='ast_per_g'>5.4</td>
                        <td data-stat='trb_per_g'>2.8</td>
                        <td data-stat='stl_per_g'>1.3</td>
                        <td data-stat='blk_per_g'>0.1</td>
                        <td data-stat='fg_per_g'>3.8</td>
                        <td data-stat='fga_per_g'>7.9</td>
                        <td data-stat='fg_pct'>.481</td>
                        <td data-stat='fg3_per_g'>1.2</td>
                        <td data-stat='fg3a_per_g'>3.1</td>
                        <td data-stat='fg3_pct'>.387</td>
                        <td data-stat='ft_per_g'>1.4</td>
                        <td data-stat='fta_per_g'>1.8</td>
                        <td data-stat='ft_pct'>.778</td>
                        <td data-stat='tov_per_g'>1.6</td>
                    </tr>
                </tbody>
            </table>
        </body></html>";

        var doc1 = new HtmlDocument();
        doc1.LoadHtml(bRefHtml);
        var bRefRows = _domParser.ParseLeagueSeasonTable(doc1, "BSL");

        Assert.Single(bRefRows);
        Assert.Equal("Berk", bRefRows[0].FirstName);
        Assert.Equal("Uğurlu", bRefRows[0].LastName);
        Assert.Equal("Beşiktaş Fibabanka", bRefRows[0].TeamName);
        Assert.Equal(28, bRefRows[0].Metrics.GamesPlayed);
        Assert.Equal(10.2m, bRefRows[0].Metrics.Points);

        string realGmHtml = @"
        <html><body>
            <table class='tablesaw'>
                <thead>
                    <tr>
                        <th>#</th><th>Player</th><th>Team</th><th>GP</th><th>MPG</th><th>PPG</th>
                        <th>FGM</th><th>FGA</th><th>FG%</th><th>3PM</th><th>3PA</th><th>3P%</th>
                        <th>FTM</th><th>FTA</th><th>FT%</th><th>ORB</th><th>DRB</th><th>RPG</th>
                        <th>APG</th><th>SPG</th><th>BPG</th><th>TOV</th><th>PF</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>1</td><td>Errick McCollum</td><td>Karsiyaka</td><td>14</td><td>29.5</td><td>22.1</td>
                        <td>6.8</td><td>14.2</td><td>.479</td><td>2.9</td><td>7.1</td><td>.408</td>
                        <td>5.6</td><td>6.2</td><td>.903</td><td>0.5</td><td>2.8</td><td>3.3</td>
                        <td>5.1</td><td>1.2</td><td>0.2</td><td>2.4</td><td>2.1</td>
                    </tr>
                </tbody>
            </table>
        </body></html>";

        var doc2 = new HtmlDocument();
        doc2.LoadHtml(realGmHtml);
        var realGmRows = _domParser.ParseLeagueSeasonTable(doc2, "BCL");

        Assert.Single(realGmRows);
        Assert.Equal("Errick", realGmRows[0].FirstName);
        Assert.Equal("McCollum", realGmRows[0].LastName);
        Assert.Equal("Karsiyaka", realGmRows[0].TeamName);
        Assert.Equal(22.1m, realGmRows[0].Metrics.Points);
        Assert.Equal(0.408m, realGmRows[0].Metrics.Fg3Pct);
    }

    [Fact]
    public async Task PlaywrightBrowserHelper_ShouldLaunchSystemBrowserSuccessfully()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await PlaywrightBrowserHelper.LaunchBrowserWithFallbackAsync(playwright, headless: true);
        Assert.NotNull(browser);
        var page = await browser.NewPageAsync();
        Assert.NotNull(page);
        await page.CloseAsync();
    }

    [Fact]
    public void BasketballReferenceDomParser_ShouldParseTeamNameAgePositionAndNationalityFromInternationalTable()
    {
        string internationalBrefHtml = @"
        <html><body>
            <table id='per_game_stats' class='stats_table'>
                <tbody>
                    <tr>
                        <td data-stat='player' data-append-csv='lual-jo01'><a href='/international/players/jo-lual-acuil-1.html'>Jo Lual-Acuil Jr.</a></td>
                        <td data-stat='age'>27</td>
                        <td data-stat='pos'>C</td>
                        <td class='left' data-stat='team_name'><a href='/international/teams/yalovaspor/2025.html'>Yalovaspor Basketbol</a></td>
                        <td data-stat='g'>7</td>
                        <td data-stat='mp_per_g'>26.4</td>
                        <td data-stat='pts_per_g'>15.3</td>
                        <td data-stat='ast_per_g'>1.1</td>
                        <td data-stat='trb_per_g'>7.9</td>
                        <td data-stat='stl_per_g'>0.6</td>
                        <td data-stat='blk_per_g'>1.7</td>
                        <td data-stat='fg_per_g'>6.1</td>
                        <td data-stat='fga_per_g'>10.3</td>
                        <td data-stat='fg_pct'>.592</td>
                        <td data-stat='fg3_per_g'>0.1</td>
                        <td data-stat='fg3a_per_g'>0.3</td>
                        <td data-stat='fg3_pct'>.500</td>
                        <td data-stat='ft_per_g'>3.0</td>
                        <td data-stat='fta_per_g'>4.4</td>
                        <td data-stat='ft_pct'>.677</td>
                        <td data-stat='tov_per_g'>1.9</td>
                        <td data-stat='nationality'>SSD</td>
                    </tr>
                </tbody>
            </table>
        </body></html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(internationalBrefHtml);
        var rows = _domParser.ParseLeagueSeasonTable(doc, "BSL");

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal("Jo", row.FirstName);
        Assert.Equal("Lual-Acuil Jr.", row.LastName);
        Assert.Equal("Yalovaspor Basketbol", row.TeamName);
        Assert.NotEqual("BSL Team", row.TeamName);
        Assert.Equal(27, row.Age);
        Assert.Equal("C", row.Position);
        Assert.Equal("SSD", row.Nationality);
    }

    [Fact]
    public void SleeperScoutingService_LowVolumeShooter_ShouldNotReceiveTransferableFloorSpacing()
    {
        // Player like Jo Lual-Acuil Jr.: Center with 0.3 3PA/G (50% 3P on 1/2 attempts in 7 games)
        var player = new Player(901, "Jo", "Lual-Acuil Jr.", "C", "7-0", 225, "Yalovaspor Basketbol", "BSL", birthDate: new DateTime(1997, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2025,
            gamesPlayed: 7,
            minutes: 26.4m,
            points: 15.3m,
            assists: 1.1m,
            rebounds: 7.9m,
            steals: 0.6m,
            blocks: 1.7m,
            fgPct: 0.592m,
            fg3Pct: 0.500m,
            ftPct: 0.677m,
            turnovers: 1.9m,
            league: "BSL",
            teamName: "Yalovaspor Basketbol",
            fgm: 6.1m,
            fga: 10.3m,
            fg3m: 0.1m,
            fg3a: 0.3m,
            ftm: 3.0m,
            fta: 4.4m
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(stat, player);

        var sleepers = _sleeperService.DiscoverSleepers(new[] { stat }, leagueFilter: "BSL");

        Assert.NotEmpty(sleepers);
        var jo = sleepers.First(s => s.PlayerId == player.Id);

        // Position, Age, and Nationality must not be null
        Assert.Equal("C", jo.Position);
        Assert.NotNull(jo.Age);
        Assert.NotNull(jo.Nationality);
        Assert.Equal("Yalovaspor Basketbol", jo.TeamName);

        // MUST NOT receive "Transferable Floor Spacing" reason because 3PA is 0.3 < 2.0 and total 3PA is 2.1 < 15.0
        Assert.DoesNotContain(jo.ScoutingReasons, r => r.Contains("Transferable Floor Spacing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SleeperScoutingService_HighPerformers_ShouldDistributeSleeperScoresWithout99_5Ceiling()
    {
        var players = new List<Player>();
        var stats = new List<PlayerSeasonStat>();

        // Create 6 different high-performing candidates with varying impacts
        for (int i = 0; i < 6; i++)
        {
            var p = new Player(1000 + i, $"Star{i}", $"Player{i}", "G-F", "6-6", 215, $"Team{i}", "BSL", birthDate: new DateTime(1999 + (i % 3), 1, 1, 0, 0, 0, DateTimeKind.Utc));
            decimal pts = 14.0m + (i * 2.5m);
            decimal fga = 9.0m + (i * 1.5m);
            decimal fg3a = 3.5m + (i * 0.5m);

            var s = new PlayerSeasonStat(
                playerId: p.Id,
                season: 2025,
                gamesPlayed: 18,
                minutes: 27.0m,
                points: pts,
                assists: 4.0m + (i * 0.8m),
                rebounds: 5.0m + (i * 0.5m),
                steals: 1.4m,
                blocks: 0.5m,
                fgPct: 0.50m,
                fg3Pct: 0.40m,
                ftPct: 0.85m,
                turnovers: 1.8m,
                league: "BSL",
                teamName: $"Team{i}",
                fgm: fga * 0.50m,
                fga: fga,
                fg3m: fg3a * 0.40m,
                fg3a: fg3a,
                ftm: 3.0m,
                fta: 3.5m
            );
            typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(s, p);
            players.Add(p);
            stats.Add(s);
        }

        var results = _sleeperService.DiscoverSleepers(stats, leagueFilter: "BSL", top: 10);

        Assert.NotEmpty(results);
        // NONE of the scores should be clamped at 99.5
        Assert.DoesNotContain(results, r => r.SleeperScore == 99.5);

        // Scores should be diversified, monotonically grading top players
        var distinctScores = results.Select(r => r.SleeperScore).Distinct().ToList();
        Assert.True(distinctScores.Count >= 4, $"Expected multiple distinct sleeper scores, but found only {distinctScores.Count} distinct scores.");

        foreach (var r in results)
        {
            Assert.InRange(r.SleeperScore, 65.0, 98.8);
            Assert.NotNull(r.Age);
            Assert.NotNull(r.Position);
            Assert.NotNull(r.Nationality);
            Assert.False(string.IsNullOrWhiteSpace(r.TeamName));
            Assert.NotEqual("BSL Team", r.TeamName);
        }
    }

    [Fact]
    public void SleeperScoutingService_MicroSample_ShouldBeFilteredOut()
    {
        // Player with only 3 games played or 7.0 minutes per game
        var p1 = new Player(801, "Low", "Games", "G", "6-2", 190, "Efes", "BSL", birthDate: new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var s1 = new PlayerSeasonStat(p1.Id, 2025, gamesPlayed: 3, minutes: 25.0m, points: 20.0m, assists: 5.0m, rebounds: 4.0m, steals: 1.0m, blocks: 0.5m, fgPct: 0.55m, fg3Pct: 0.45m, ftPct: 0.90m, turnovers: 1.0m, league: "BSL", teamName: "Efes");
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(s1, p1);

        var p2 = new Player(802, "Low", "Minutes", "G", "6-2", 190, "Efes", "BSL", birthDate: new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var s2 = new PlayerSeasonStat(p2.Id, 2025, gamesPlayed: 15, minutes: 6.5m, points: 8.0m, assists: 2.0m, rebounds: 2.0m, steals: 0.5m, blocks: 0.1m, fgPct: 0.55m, fg3Pct: 0.45m, ftPct: 0.90m, turnovers: 0.5m, league: "BSL", teamName: "Efes");
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(s2, p2);

        var discoveries = _sleeperService.DiscoverSleepers(new[] { s1, s2 }, leagueFilter: "BSL");
        Assert.Empty(discoveries);
    }

    [Fact]
    public void BasketballReferenceDomParser_RealGmTable_ShouldParsePlayerStatsCorrectly()
    {
        string realGmHtml = @"
        <table class=""table table-striped table-centered table-hover table-bordered table-compact table-nowrap"">
            <thead>
                <tr>
                    <th>#</th>
                    <th><a href=""#"">Player</a></th>
                    <th><a href=""#"">Team</a></th>
                    <th><a href=""#"">GP</a></th>
                    <th><a href=""#"">MPG</a></th>
                    <th><a href=""#"">PPG</a></th>
                    <th><a href=""#"">FGM</a></th>
                    <th><a href=""#"">FGA</a></th>
                    <th><a href=""#"">FG%</a></th>
                    <th><a href=""#"">3PM</a></th>
                    <th><a href=""#"">3PA</a></th>
                    <th><a href=""#"">3P%</a></th>
                    <th><a href=""#"">FTM</a></th>
                    <th><a href=""#"">FTA</a></th>
                    <th><a href=""#"">FT%</a></th>
                    <th><a href=""#"">ORB</a></th>
                    <th><a href=""#"">DRB</a></th>
                    <th><a href=""#"">RPG</a></th>
                    <th><a href=""#"">APG</a></th>
                    <th><a href=""#"">SPG</a></th>
                    <th><a href=""#"">BPG</a></th>
                    <th><a href=""#"">TOV</a></th>
                    <th><a href=""#"">PF</a></th>
                </tr>
            </thead>
            <tbody>
                <tr>
                    <td>1.</td>
                    <td><a href=""/international/player/Kendrick-Perry/summary/30291"">Kendrick Perry</a></td>
                    <td><a href=""/international/teams/Unicaja-Malaga/stats"">UNI</a></td>
                    <td>12</td>
                    <td>23.5</td>
                    <td>14.8</td>
                    <td>5.2</td>
                    <td>10.4</td>
                    <td>.500</td>
                    <td>2.1</td>
                    <td>5.0</td>
                    <td>.420</td>
                    <td>2.3</td>
                    <td>2.8</td>
                    <td>.821</td>
                    <td>0.5</td>
                    <td>2.0</td>
                    <td>2.5</td>
                    <td>4.8</td>
                    <td>1.3</td>
                    <td>0.1</td>
                    <td>1.8</td>
                    <td>2.2</td>
                </tr>
            </tbody>
        </table>";

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(realGmHtml);

        var rows = _domParser.ParseLeagueSeasonTable(doc, "BCL");
        Assert.Single(rows);

        var row = rows[0];
        Assert.Equal("Kendrick", row.FirstName);
        Assert.Equal("Perry", row.LastName);
        Assert.Equal("UNI", row.TeamName);
        Assert.NotNull(row.Metrics);
        Assert.Equal(12, row.Metrics.GamesPlayed);
        Assert.Equal(23.5m, row.Metrics.Minutes);
        Assert.Equal(14.8m, row.Metrics.Points);
        Assert.Equal(4.8m, row.Metrics.Assists);
        Assert.Equal(2.5m, row.Metrics.Rebounds);
        Assert.Equal(1.3m, row.Metrics.Steals);
        Assert.Equal(0.500m, row.Metrics.FgPct);
        Assert.Equal(0.420m, row.Metrics.Fg3Pct);
        Assert.Equal(0.821m, row.Metrics.FtPct);
        Assert.Equal(1.8m, row.Metrics.Turnovers);
    }

    [Fact]
    public void GlobalLeagueEndpointCatalog_6FailedLeagues_ShouldHaveVerifiedSlugsAndIds()
    {
        // 1. BCL -> RealGM ID 106
        var bcl = GlobalLeagueEndpointCatalog.GetByCode("BCL");
        Assert.NotNull(bcl);
        Assert.Contains("106/Basketball-Champions-League-Europe", bcl.PrimaryUrlTemplate);

        // 2. ABA -> B-Ref aba-adriatic and RealGM ID 18
        var aba = GlobalLeagueEndpointCatalog.GetByCode("ABA");
        Assert.NotNull(aba);
        Assert.Contains("aba-adriatic", aba.PrimaryUrlTemplate);
        Assert.Contains("18/Adriatic-League-Liga-ABA", aba.FallbackUrlTemplate);

        // 3. BBL -> RealGM ID 15
        var bbl = GlobalLeagueEndpointCatalog.GetByCode("BBL");
        Assert.NotNull(bbl);
        Assert.Contains("15/German-BBL", bbl.PrimaryUrlTemplate);

        // 4. GBL -> B-Ref greek-basket-league and RealGM ID 8
        var gbl = GlobalLeagueEndpointCatalog.GetByCode("GBL");
        Assert.NotNull(gbl);
        Assert.Contains("greek-basket-league", gbl.PrimaryUrlTemplate);
        Assert.Contains("8/Greek-HEBA-A1", gbl.FallbackUrlTemplate);

        // 5. LKL -> RealGM ID 10
        var lkl = GlobalLeagueEndpointCatalog.GetByCode("LKL");
        Assert.NotNull(lkl);
        Assert.Contains("10/Lithuanian-LKL", lkl.PrimaryUrlTemplate);

        // 6. VTB -> B-Ref vtb-united and RealGM ID 35
        var vtb = GlobalLeagueEndpointCatalog.GetByCode("VTB");
        Assert.NotNull(vtb);
        Assert.Contains("vtb-united", vtb.PrimaryUrlTemplate);
        Assert.Contains("35/VTB-United-League", vtb.FallbackUrlTemplate);
    }

    [Fact]
    public void SleeperScoutingService_UnknownBirthDate_ShouldLeaveAgeAsNullWithoutHardcoding26()
    {
        // Player with completely unknown birth date
        var player = new Player(9999, "Unknown", "Veteran", "F", "6-8", 220, "Fenerbahçe Beko", "BSL", birthDate: null);
        Assert.Null(player.BirthDate);

        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2025,
            gamesPlayed: 20,
            minutes: 24.0m,
            points: 16.0m,
            assists: 3.5m,
            rebounds: 7.0m,
            steals: 1.2m,
            blocks: 0.8m,
            fgPct: 0.52m,
            fg3Pct: 0.38m,
            ftPct: 0.82m,
            turnovers: 1.5m,
            league: "BSL",
            teamName: "Fenerbahçe Beko",
            fgm: 6.0m,
            fga: 11.5m,
            fg3m: 1.5m,
            fg3a: 4.0m,
            ftm: 2.5m,
            fta: 3.0m
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(stat, player);

        var discoveries = _sleeperService.DiscoverSleepers(new[] { stat }, leagueFilter: "BSL");
        Assert.Single(discoveries);

        var dto = discoveries.First();
        Assert.Null(dto.Age); // MUST BE NULL - never hardcoded to 26!
        Assert.Equal("Unknown Veteran", dto.FullName);
    }

    [Fact]
    public void SleeperScoutingService_PlayerWithMultipleCategories_ShouldBeDeduplicatedInOutput()
    {
        // A player with versatile profile that could match high-efficiency and high-volume sleeper criteria
        var player = new Player(8888, "Elite", "Prospect", "G", "6-5", 205, "Anadolu Efes", "BSL", birthDate: new DateTime(2003, 4, 10, 0, 0, 0, DateTimeKind.Utc));

        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2025,
            gamesPlayed: 22,
            minutes: 26.0m,
            points: 17.5m,
            assists: 5.5m,
            rebounds: 4.5m,
            steals: 1.8m,
            blocks: 0.4m,
            fgPct: 0.54m,
            fg3Pct: 0.41m,
            ftPct: 0.88m,
            turnovers: 1.6m,
            league: "BSL",
            teamName: "Anadolu Efes",
            fgm: 6.5m,
            fga: 12.0m,
            fg3m: 2.5m,
            fg3a: 6.0m,
            ftm: 2.0m,
            fta: 2.3m
        );
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(stat, player);

        var discoveries = _sleeperService.DiscoverSleepers(new[] { stat }, leagueFilter: "BSL", top: 10);
        // Player should only appear ONCE in results despite qualifying for multiple categories
        var matching = discoveries.Where(d => d.FullName == "Elite Prospect").ToList();
        Assert.Single(matching);
        Assert.Equal(22, matching.First().Age); // 2025 - 2003 = 22
    }

    [Fact]
    public void GlobalLeagueEndpointCatalog_EuroLeagueAndEuroCup_ShouldUseFastTableScraping()
    {
        var euroLeague = GlobalLeagueEndpointCatalog.GetByCode("EuroLeague");
        Assert.NotNull(euroLeague);
        Assert.Equal("BasketballReferenceInternational", euroLeague.PrimaryProvider);
        Assert.Contains("international/euroleague/{year}_per_game.html", euroLeague.PrimaryUrlTemplate);
        Assert.Contains("realgm.com", euroLeague.FallbackUrlTemplate);

        var euroCup = GlobalLeagueEndpointCatalog.GetByCode("EuroCup");
        Assert.NotNull(euroCup);
        Assert.Equal("BasketballReferenceInternational", euroCup.PrimaryProvider);
        Assert.Contains("international/eurocup/{year}_per_game.html", euroCup.PrimaryUrlTemplate);
        Assert.Contains("realgm.com", euroCup.FallbackUrlTemplate);
    }

    [Fact]
    public void GlobalLeagueEndpointCatalog_GetTableScrapedLeagues_ShouldContainAllLeaguesInOrder()
    {
        var tableLeagues = GlobalLeagueEndpointCatalog.GetTableScrapedLeagues();
        var leagueCodes = tableLeagues.Select(l => l.LeagueCode).ToList();

        string[] expected14 = { "NBA", "EuroLeague", "EuroCup", "ACB", "BSL", "BCL", "ABA", "BBL", "LNB", "LegaA", "GBL", "LKL", "GLeague", "VTB" };
        foreach (var expected in expected14)
        {
            Assert.Contains(expected, leagueCodes);
        }

        // NBA is now fast-table-scraped alongside all leagues
        Assert.Contains("NBA", leagueCodes);

        // First 14 leagues should match the canonical order
        for (int i = 0; i < expected14.Length; i++)
        {
            Assert.Equal(expected14[i], leagueCodes[i]);
        }
    }

    [Fact]
    public void SyncFilterContext_LeagueFiltering_ShouldSupportAllAndCommaSeparatedLeagues()
    {
        // 1. null / empty / "all" matches all candidate leagues
        var nullFilter = new SyncFilterContext { LeagueCode = null };
        Assert.True(nullFilter.IsAllLeagues);
        Assert.True(nullFilter.ShouldProcessLeague("EuroLeague"));
        Assert.True(nullFilter.ShouldProcessLeague("BSL"));
        Assert.True(nullFilter.ShouldProcessLeague("ACB"));
        Assert.False(nullFilter.HasExplicitLeague("NBA")); // NBA excluded from all-league sweeps

        var allFilter = new SyncFilterContext { LeagueCode = "all" };
        Assert.True(allFilter.IsAllLeagues);
        Assert.True(allFilter.ShouldProcessLeague("EuroLeague"));
        Assert.True(allFilter.ShouldProcessLeague("BSL"));

        // 2. Comma-separated leagues
        var multiFilter = new SyncFilterContext { LeagueCode = "BSL, EuroLeague, ACB" };
        Assert.False(multiFilter.IsAllLeagues);
        Assert.True(multiFilter.ShouldProcessLeague("BSL"));
        Assert.True(multiFilter.ShouldProcessLeague("EuroLeague"));
        Assert.True(multiFilter.ShouldProcessLeague("ACB"));
        Assert.False(multiFilter.ShouldProcessLeague("BBL"));
        Assert.False(multiFilter.ShouldProcessLeague("VTB"));

        // 3. Single league
        var singleFilter = new SyncFilterContext { LeagueCode = "BSL" };
        Assert.True(singleFilter.ShouldProcessLeague("BSL"));
        Assert.False(singleFilter.ShouldProcessLeague("EuroLeague"));

        // 4. Explicit NBA filter
        var nbaFilter = new SyncFilterContext { LeagueCode = "NBA" };
        Assert.True(nbaFilter.HasExplicitLeague("NBA"));
        Assert.True(nbaFilter.ShouldProcessLeague("NBA"));
    }

    [Fact]
    public void SyncFilterContext_ForceResync_ShouldBypassCompletedCheckpoints()
    {
        var completedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SyncFilterContext.BuildJobKey("BSL", 2025)
        };

        // When force=false, completed seasons should be skipped
        var normalFilter = new SyncFilterContext
        {
            LeagueCode = "BSL",
            Season = 2025,
            Force = false,
            CompletedJobKeys = completedKeys
        };
        Assert.True(normalFilter.IsAlreadyCompleted("BSL", 2025));

        // When force=true, completed seasons are re-processed
        var forceFilter = new SyncFilterContext
        {
            LeagueCode = "BSL",
            Season = 2025,
            Force = true,
            CompletedJobKeys = completedKeys
        };
        Assert.False(forceFilter.IsAlreadyCompleted("BSL", 2025));
    }

    [Fact]
    public void BasketballReferenceDomParser_WithModernGamesAndGamesStartedAttributes_ShouldExtractCorrectly()
    {
        var html = @"
        <html>
        <body>
            <table id='per_game'>
                <tbody>
                    <tr>
                        <th data-stat='year_id'>2023-24</th>
                        <td data-stat='games'>75</td>
                        <td data-stat='games_started'>72</td>
                        <td data-stat='mp_per_g'>35.2</td>
                        <td data-stat='pts_per_g'>26.4</td>
                    </tr>
                </tbody>
            </table>
        </body>
        </html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var parser = new BasketballReferenceDomParser();
        var result = parser.ParsePerGameTable(doc);

        Assert.Single(result);
        Assert.True(result.ContainsKey(2024));
        Assert.Equal(75, result[2024].GamesPlayed);
        Assert.Equal(72, result[2024].GamesStarted);
        Assert.Equal(26.4m, result[2024].Points);
    }

    [Fact]
    public void BasketballReferenceDomParser_NbaLeagueTable_ShouldParsePlayerStatsCorrectly()
    {
        var html = @"
        <html>
        <body>
            <table id='per_game_stats'>
                <tbody>
                    <tr data-row='0'>
                        <td class='left' data-append-csv='embiijo01' data-stat='name_display'><a href='/players/e/embiijo01.html'>Joel Embiid</a></td>
                        <td class='right' data-stat='age'>29</td>
                        <td class='left' data-stat='team_name_abbr'><a href='/teams/PHI/2024.html'>PHI</a></td>
                        <td class='center' data-stat='pos'>C</td>
                        <td class='right' data-stat='games'>39</td>
                        <td class='right' data-stat='games_started'>39</td>
                        <td class='right' data-stat='mp_per_g'>33.6</td>
                        <td class='right' data-stat='fg_per_g'>11.5</td>
                        <td class='right' data-stat='fga_per_g'>21.8</td>
                        <td class='right' data-stat='fg_pct'>.529</td>
                        <td class='right' data-stat='fg3_per_g'>1.4</td>
                        <td class='right' data-stat='fg3a_per_g'>3.6</td>
                        <td class='right' data-stat='fg3_pct'>.388</td>
                        <td class='right' data-stat='efg_pct'>.561</td>
                        <td class='right' data-stat='ft_per_g'>10.2</td>
                        <td class='right' data-stat='fta_per_g'>11.6</td>
                        <td class='right' data-stat='ft_pct'>.883</td>
                        <td class='right' data-stat='orb_per_g'>2.4</td>
                        <td class='right' data-stat='drb_per_g'>8.6</td>
                        <td class='right' data-stat='trb_per_g'>11.0</td>
                        <td class='right' data-stat='ast_per_g'>5.6</td>
                        <td class='right' data-stat='stl_per_g'>1.2</td>
                        <td class='right' data-stat='blk_per_g'>1.7</td>
                        <td class='right' data-stat='tov_per_g'>3.8</td>
                        <td class='right' data-stat='pf_per_g'>2.9</td>
                        <td class='right' data-stat='pts_per_g'>34.7</td>
                    </tr>
                </tbody>
            </table>
        </body>
        </html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var parser = new BasketballReferenceDomParser();
        var rows = parser.ParseLeagueSeasonTable(doc, "NBA");

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal("Joel", row.FirstName);
        Assert.Equal("Embiid", row.LastName);
        Assert.Equal("PHI", row.TeamName);
        Assert.Equal("C", row.Position);
        Assert.Equal(29, row.Age);
        Assert.Equal(39, row.Metrics.GamesPlayed);
        Assert.Equal(39, row.Metrics.GamesStarted);
        Assert.Equal(33.6m, row.Metrics.Minutes);
        Assert.Equal(34.7m, row.Metrics.Points);
        Assert.Equal(11.5m, row.Metrics.Fgm);
        Assert.Equal(21.8m, row.Metrics.Fga);
        Assert.Equal(0.561m, row.Metrics.EffectiveFgPct);
    }

    [Fact(Skip = "Explicit integration runner for modern NBA seasons")]
    public async Task LiveSync_ModernNbaSeasons_FastTableSync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var dbOptions = new DbContextOptionsBuilder<SmartScoutDbContext>()
            .UseNpgsql("Host=localhost;Database=SmartScoutDb;Username=postgres;Password=123")
            .Options;
        await using var db = new SmartScoutDbContext(dbOptions);

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalLeagueTableScrapingStrategy>.Instance;
        var scraperOpts = Microsoft.Extensions.Options.Options.Create(new ScraperOptions
        {
            Headless = true,
            DelayBetweenRequestsMs = 3000,
            EuroSeasons = new[] { 2020, 2021, 2022, 2023, 2024, 2025 }
        });
        var domParser = new BasketballReferenceDomParser();
        var strategy = new GlobalLeagueTableScrapingStrategy(logger, scraperOpts, domParser);

        var handler = new SyncSeasonStatsCommandHandler(new ISeasonStatSyncStrategy[] { strategy }, db);

        int[] seasonsToSync = { 2020, 2021, 2022, 2023, 2025 };
        foreach (int seasonYear in seasonsToSync)
        {
            var command = new SyncSeasonStatsCommand { League = "NBA", Season = seasonYear.ToString(), Force = true };
            await handler.Handle(command, CancellationToken.None);
        }
    }

    [Fact]
    public void Player_WatchlistAndNotes_CanBeUpdatedAndToggled()
    {
        var player = new Player(88001, "Tarik", "Biberovic", "SF", "6-7", 218, "Fenerbahce Beko", "EuroLeague");
        
        Assert.False(player.IsWatchlisted);
        Assert.Null(player.ScoutNotes);

        player.ToggleWatchlist();
        Assert.True(player.IsWatchlisted);

        player.SetScoutNotes("Elite catch-and-shoot wing with modern 3-and-D size.");
        Assert.Equal("Elite catch-and-shoot wing with modern 3-and-D size.", player.ScoutNotes);

        player.UpdateScoutingIntel(
            standingReachCm: 268,
            contractExpirySeason: 2026,
            estimatedMarketValueTier: "Tier 2 • EuroLeague Starter / High-Impact",
            agencyName: "BeoBasket / Octagon");

        Assert.Equal(268, player.StandingReachCm);
        Assert.Equal(2026, player.ContractExpirySeason);
        Assert.Equal("Tier 2 • EuroLeague Starter / High-Impact", player.EstimatedMarketValueTier);
        Assert.Equal("BeoBasket / Octagon", player.AgencyName);

        player.ToggleWatchlist();
        Assert.False(player.IsWatchlisted);
    }

    [Fact]
    public void StatCalculationService_CalculatesStocksPer36AndThreePointRate_Automatically()
    {
        var player = new Player(88002, "Mychal", "Mulder", "SG", "6-3", 184, "Capital City Go-Go", "GLeague");
        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2026,
            gamesPlayed: 30,
            minutes: 24.0m,
            points: 12.0m,
            assists: 3.0m,
            rebounds: 5.0m,
            steals: 1.2m,
            blocks: 0.8m,
            fgPct: 0.46m,
            fg3Pct: 0.38m,
            ftPct: 0.80m,
            turnovers: 1.5m,
            fga: 10.0m,
            fg3a: 4.5m);

        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(stat, player);
        StatCalculationService.EnrichAndCalculate(stat);

        Assert.NotNull(stat.StocksPer36);
        Assert.True(stat.StocksPer36 > 0m);
        Assert.Equal(0.450m, stat.ThreePointRate); // 4.5 / 10.0
    }

    [Fact]
    public void StatCalculationService_EuroLeagueTransitionIndex_EvaluatesLeaguesCorrectly()
    {
        // NBA stat -> 98.0
        var nbaStat = new PlayerSeasonStat(Guid.NewGuid(), 2026, 60, 20.0m, 10.0m, 2.0m, 4.0m, 1.0m, 0.5m, 0.45m, 0.35m, 0.80m, 1.0m, league: "NBA");
        var nbaEti = StatCalculationService.CalculateEuroLeagueTransitionIndex(nbaStat, 25, 201);
        Assert.Equal(98.0m, nbaEti);

        // EuroLeague stat -> 94.0
        var elStat = new PlayerSeasonStat(Guid.NewGuid(), 2026, 30, 25.0m, 14.0m, 3.0m, 4.0m, 1.2m, 0.4m, 0.48m, 0.40m, 0.85m, 1.5m, league: "EuroLeague");
        var elEti = StatCalculationService.CalculateEuroLeagueTransitionIndex(elStat, 26, 198);
        Assert.Equal(94.0m, elEti);

        // Tier-2 EuroCup prospect
        var ecStat = new PlayerSeasonStat(Guid.NewGuid(), 2026, 20, 26.0m, 15.0m, 3.5m, 3.5m, 1.5m, 0.3m, 0.47m, 0.38m, 0.82m, 1.8m, league: "EuroCup");
        var ecEti = StatCalculationService.CalculateEuroLeagueTransitionIndex(ecStat, 24, 203);
        Assert.NotNull(ecEti);
        Assert.True(ecEti > 75.0m && ecEti < 95.0m);
    }

    [Fact]
    public void PlayerAnalyticsService_PositionalPercentilesAndShotDiet_ShouldComputeAccurately()
    {
        var analytics = new PlayerAnalyticsService();
        var guard = new Player(91001, "Shane", "LarkinClone", "PG", "6-0", 175, "Anadolu Efes", "EuroLeague", birthDate: new DateTime(1998, 10, 2, 0, 0, 0, DateTimeKind.Utc));
        var stat = new PlayerSeasonStat(
            playerId: guard.Id,
            season: 2026,
            gamesPlayed: 28,
            minutes: 28.0m,
            points: 17.5m,
            assists: 5.5m,
            rebounds: 2.8m,
            steals: 1.4m,
            blocks: 0.1m,
            fgPct: 0.48m,
            fg3Pct: 0.41m,
            ftPct: 0.90m,
            turnovers: 1.8m,
            foulsDrawn: 4.5m,
            league: "EuroLeague",
            teamName: "Anadolu Efes",
            fgm: 5.5m, fga: 11.4m, fg3m: 2.8m, fg3a: 6.8m, fg2m: 2.7m, fg2a: 4.6m, ftm: 3.7m, fta: 4.1m,
            orb: 0.6m, drb: 2.2m, pfCommitted: 1.8m);

        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(stat, guard);
        StatCalculationService.EnrichAndCalculate(stat);

        var percentiles = analytics.CalculatePositionalPercentiles(stat, "PG");
        Assert.InRange(percentiles["PtsPer36"], 65, 99);
        Assert.InRange(percentiles["TrueShootingPct"], 70, 99);
        Assert.InRange(percentiles["Fg3Pct"], 70, 99);
        Assert.InRange(percentiles["FoulDiscipline"], 60, 99);

        var dto = new PlayerScoutingReportDto();
        analytics.PopulateShotDietAndPhysicality(dto, stat);

        Assert.NotNull(dto.FreeThrowRate);
        Assert.True(dto.FreeThrowRate > 0.30m);
        Assert.NotNull(dto.FoulsCommittedPer36);
        Assert.Equal("Low Risk", dto.FibaFoulTroubleRisk);
        Assert.Equal(100.0, Math.Round(dto.TwoPointScoringSharePct + dto.ThreePointScoringSharePct + dto.FreeThrowScoringSharePct, 0));
    }

    [Fact]
    public void SmartScoutSearchEngine_MultiConstraintNlpParser_ShouldFilterByAgeHeightContractAndPercentage()
    {
        var analytics = new PlayerAnalyticsService();
        var engine = new SmartScoutSearchEngine(analytics);

        // Candidate 1: U24, 206cm Stretch Big, 2026 expiring contract, 40% 3PT, low turnovers
        var pMatch = new Player(
            externalApiId: 92001,
            firstName: "Ercan",
            lastName: "OsmaniClone",
            position: "PF-C",
            height: "6-9",
            weightPounds: 235,
            teamName: "Anadolu Efes",
            league: "EuroLeague",
            nationality: "TUR",
            birthDate: new DateTime(2003, 8, 4, 0, 0, 0, DateTimeKind.Utc),
            heightCm: 206,
            weightKg: 107,
            wingspanCm: 216);
        pMatch.UpdateScoutingIntel(272, 2026, "Tier 2 • EuroLeague Starter", "IstBasket");

        var sMatch = new PlayerSeasonStat(
            pMatch.Id, 2026, 24, 22.0m, 12.5m, 2.4m, 6.5m, 1.1m, 0.9m, 0.51m, 0.40m, 0.82m, 1.0m,
            league: "EuroLeague", teamName: "Anadolu Efes", fga: 8.5m, fg3a: 4.0m, pfCommitted: 2.0m);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(sMatch, pMatch);
        StatCalculationService.EnrichAndCalculate(sMatch);

        // Candidate 2: Older (31 yrs), 198cm guard -> should be filtered out by U24 and 205+ cm
        var pOld = new Player(
            externalApiId: 92002,
            firstName: "Veteran",
            lastName: "Shooter",
            position: "SG",
            height: "6-6",
            weightPounds: 205,
            teamName: "Fenerbahce",
            league: "EuroLeague",
            nationality: "TUR",
            birthDate: new DateTime(1994, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            heightCm: 198,
            weightKg: 93);
        pOld.UpdateScoutingIntel(258, 2027, "Tier 1", "CAA");

        var sOld = new PlayerSeasonStat(
            pOld.Id, 2026, 25, 24.0m, 14.0m, 3.0m, 3.0m, 1.0m, 0.2m, 0.48m, 0.42m, 0.88m, 2.2m,
            league: "EuroLeague", teamName: "Fenerbahce", fga: 10.0m, fg3a: 6.0m);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(sOld, pOld);
        StatCalculationService.EnrichAndCalculate(sOld);

        var results = engine.Search(
            "U24 205+ cm yerli 2026 contract %38+ 3pt düşük top kaybı",
            new[] { sMatch, sOld });

        var matched = Assert.Single(results);
        Assert.Equal(pMatch.Id, matched.PlayerId);
        Assert.NotEmpty(matched.ParsedConstraints);
        Assert.Contains(matched.ParsedConstraints, c => c.Contains("U24"));
        Assert.Contains(matched.ParsedConstraints, c => c.Contains("205cm"));
        Assert.Contains(matched.ParsedConstraints, c => c.Contains("2026"));
    }

    [Fact]
    public void PlayerAnalyticsService_Compare_ShouldPopulateExtendedDifferentialsAndScoutingVerdict()
    {
        var analytics = new PlayerAnalyticsService();
        var pA = new Player(93001, "Alpha", "Guard", "PG", "6-3", 195, "Real Madrid", "EuroLeague", birthDate: new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var pB = new Player(93002, "Beta", "Guard", "PG", "6-4", 200, "Olympiacos", "EuroLeague", birthDate: new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var sA = new PlayerSeasonStat(pA.Id, 2026, 25, 25.0m, 16.0m, 6.0m, 3.0m, 1.5m, 0.2m, 0.49m, 0.39m, 0.86m, 1.8m, league: "EuroLeague");
        var sB = new PlayerSeasonStat(pB.Id, 2026, 25, 24.0m, 12.0m, 4.5m, 4.2m, 1.1m, 0.4m, 0.45m, 0.35m, 0.80m, 1.9m, league: "EuroLeague");
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(sA, pA);
        typeof(PlayerSeasonStat).GetProperty("Player")!.SetValue(sB, pB);
        StatCalculationService.EnrichAndCalculate(sA);
        StatCalculationService.EnrichAndCalculate(sB);

        var comp = analytics.Compare(sA, pA, sB, pB);

        Assert.False(string.IsNullOrWhiteSpace(comp.ScoutingVerdict));
        Assert.Contains("Alpha Guard", comp.ScoutingVerdict);
        Assert.NotEmpty(comp.PlayerA.Percentiles);
        Assert.NotEmpty(comp.PlayerB.Percentiles);
        Assert.True(comp.Differentials.ContainsKey("PtsPer36"));
        Assert.True(comp.Differentials.ContainsKey("StocksPer36"));
    }
}
