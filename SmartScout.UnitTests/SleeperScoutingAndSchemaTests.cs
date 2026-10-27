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
}
