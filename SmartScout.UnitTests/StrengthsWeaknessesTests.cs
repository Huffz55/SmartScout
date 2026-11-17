using System;
using System.Collections.Generic;
using SmartScout.Application.DTOs;
using SmartScout.Application.Services;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Services.Reporting;
using Xunit;

namespace SmartScout.UnitTests;

public class StrengthsWeaknessesTests
{
    [Fact]
    public void AnalyzeStrengthsAndWeaknesses_ShouldDetectEliteShootingPlaymakingAndDefense()
    {
        var analytics = new PlayerAnalyticsService();
        var player = new Player(
            externalApiId: 777001,
            firstName: "Luka",
            lastName: "Prospect",
            position: "PG",
            height: null,
            weightPounds: null,
            teamName: "Partizan",
            league: "ABA",
            nationality: "SRB",
            birthDate: new DateTime(2005, 3, 15, 0, 0, 0, DateTimeKind.Utc));

        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2027,
            gamesPlayed: 26,
            minutes: 28.0m,
            points: 16.5m,
            assists: 6.2m,
            rebounds: 4.1m,
            steals: 1.8m,
            blocks: 0.4m,
            fgPct: 0.485m,
            fg3Pct: 0.418m,
            ftPct: 0.875m,
            turnovers: 2.1m,
            foulsDrawn: 4.1m,
            league: "ABA",
            teamName: "Partizan",
            fgm: 5.5m,
            fga: 11.3m,
            fg3m: 2.3m,
            fg3a: 5.5m,
            fg2m: 3.2m,
            fg2a: 5.8m,
            ftm: 3.2m,
            fta: 3.7m,
            orb: 0.7m,
            drb: 3.4m,
            pfCommitted: 1.7m);
        stat.SetTransitionIndex(84.5m);
        player.AddSeasonStat(stat);

        var (strengths, weaknesses) = analytics.AnalyzeStrengthsAndWeaknesses(stat, player);

        Assert.NotEmpty(strengths);
        Assert.Contains(strengths, s => s.Category == "Shooting" && s.Title.Contains("Spacing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(strengths, s => s.Category == "Playmaking");
        Assert.Contains(strengths, s => s.Category == "Defense" || s.Category == "Scoring" || s.Category == "Efficiency");
        Assert.NotEmpty(weaknesses);
    }

    [Fact]
    public void AnalyzeStrengthsAndWeaknesses_ShouldFlagActiveInjuryFoulTroubleAndShootingRisk()
    {
        var analytics = new PlayerAnalyticsService();
        var player = new Player(
            externalApiId: 777002,
            firstName: "Risk",
            lastName: "Wing",
            position: "SF",
            height: null,
            weightPounds: null,
            teamName: "Demo Club",
            league: "BSL",
            nationality: "USA",
            birthDate: new DateTime(1999, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2027,
            gamesPlayed: 18,
            minutes: 18.0m,
            points: 6.5m,
            assists: 1.5m,
            rebounds: 3.2m,
            steals: 0.5m,
            blocks: 0.1m,
            fgPct: 0.380m,
            fg3Pct: 0.250m,
            ftPct: 0.610m,
            turnovers: 2.7m,
            foulsDrawn: 1.4m,
            league: "BSL",
            teamName: "Demo Club",
            fgm: 2.4m,
            fga: 6.3m,
            fg3m: 0.7m,
            fg3a: 2.8m,
            fg2m: 1.7m,
            fg2a: 3.5m,
            ftm: 1.0m,
            fta: 1.6m,
            orb: 0.6m,
            drb: 2.6m,
            pfCommitted: 2.3m);
        player.AddSeasonStat(stat);
        player.AddInjury(new PlayerInjury(player.Id, 2027, "Left Ankle Sprain", "Out", new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), source: "ESPN NBA"));

        var (strengths, weaknesses) = analytics.AnalyzeStrengthsAndWeaknesses(stat, player);

        Assert.Contains(weaknesses, w => w.Category == "Medical" && w.MetricBadge.Contains("Out", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(weaknesses, w => w.Category == "Discipline" && w.Title.Contains("Foul-Trouble", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(weaknesses, w => w.Category == "Shooting");
        Assert.NotEmpty(strengths);
    }

    [Fact]
    public void QuestPdfGenerator_ShouldRenderPdfSuccessfully_WithStrengthsAndWeaknesses()
    {
        var generator = new QuestPdfScoutingReportGenerator();
        var model = new PlayerScoutingReportDto
        {
            PlayerId = Guid.NewGuid(),
            FirstName = "Nikola",
            LastName = "Tester",
            Position = "PG",
            League = "EuroLeague",
            Season = 2027,
            Archetype = "FloorGeneral",
            RadarMetrics = new RadarMetricsDto(75, 80, 88, 45, 68, 79),
            TacticalVerdict = "High-IQ primary initiator ready for EuroLeague half-court basketball.",
            Strengths = new List<ScoutingTraitDto>
            {
                new() { Title = "Primary Pick-and-Roll Creator", MetricBadge = "8.1 AST/36", Detail = "Reads weak-side tags consistently.", Category = "Playmaking" }
            },
            Weaknesses = new List<ScoutingTraitDto>
            {
                new() { Title = "FIBA Foul-Trouble Risk", MetricBadge = "4.1 PF/36", Detail = "Hand-check discipline needs adjustment.", Category = "Discipline" }
            }
        };

        byte[] pdf = generator.GenerateReportPdf(model);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void GenerateComparisonPdf_ProducesValidPdfDocument()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var generator = new QuestPdfScoutingReportGenerator();

        var comparison = new PlayerComparisonDto
        {
            SimilarityScore = 84.5,
            ScoutingVerdict = "Comparable perimeter scoring profiles with stronger defensive activity for Player A.",
            PlayerA = new PlayerComparisonProfileDto
            {
                PlayerId = Guid.NewGuid(),
                FullName = "Alex Guard",
                League = "EuroLeague",
                TeamName = "Fenerbahçe",
                Position = "PG",
                Season = 2027,
                Archetype = "Pick-and-Roll Maestro",
                GamesPlayed = 28,
                Age = 24,
                HeightCm = 191,
                WeightKg = 86,
                Points = 14.2m,
                PtsPer36 = 18.6m,
                TrueShootingPct = 0.612m,
                Fg3Pct = 0.395m,
                AstPer36 = 6.8m,
                RebPer36 = 4.1m,
                StocksPer36 = 1.9m,
                EuroLeagueTransitionIndex = 82.4m,
                EstimatedMarketValueTier = "Tier 2 • Prime Starter",
                RadarMetrics = new RadarMetricsDto(78, 82, 85, 48, 72, 81),
                Strengths = new List<ScoutingTraitDto>
                {
                    new() { Category = "Playmaking", Title = "High-Volume P&R Creator", Detail = "6.8 AST/36 with low turnover pressure.", MetricBadge = "6.8 AST/36" }
                },
                Weaknesses = new List<ScoutingTraitDto>
                {
                    new() { Category = "Rebounding", Title = "Below-Average Glass Work", Detail = "4.1 REB/36 limits switchability against big guards.", MetricBadge = "4.1 REB/36" }
                }
            },
            PlayerB = new PlayerComparisonProfileDto
            {
                PlayerId = Guid.NewGuid(),
                FullName = "Jordan Wing",
                League = "ACB",
                TeamName = "Real Madrid",
                Position = "SG",
                Season = 2027,
                Archetype = "3-and-D Wing",
                GamesPlayed = 26,
                Age = 25,
                HeightCm = 198,
                WeightKg = 94,
                Points = 12.4m,
                PtsPer36 = 16.5m,
                TrueShootingPct = 0.594m,
                Fg3Pct = 0.381m,
                AstPer36 = 3.2m,
                RebPer36 = 5.4m,
                StocksPer36 = 2.2m,
                EuroLeagueTransitionIndex = 79.1m,
                EstimatedMarketValueTier = "Tier 2 • Prime Starter",
                RadarMetrics = new RadarMetricsDto(70, 77, 52, 62, 84, 76),
                Strengths = new List<ScoutingTraitDto>
                {
                    new() { Category = "Defense", Title = "Switchable Wing Stopper", Detail = "2.2 stocks/36 across multiple perimeter assignments.", MetricBadge = "2.2 STK/36" }
                },
                Weaknesses = new List<ScoutingTraitDto>
                {
                    new() { Category = "Playmaking", Title = "Limited Secondary Creation", Detail = "3.2 AST/36 relies on catch-and-shoot reads.", MetricBadge = "3.2 AST/36" }
                }
            }
        };

        byte[] pdf = generator.GenerateComparisonPdf(comparison);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);
    }

    [Fact]
    public void ScoutBoard_CreatesEntriesAndUpdatesNotes()
    {
        var board = new ScoutBoard("  EuroLeague Shortlist 2027  ", "  Mehmet  ");
        Assert.Equal("EuroLeague Shortlist 2027", board.Name);
        Assert.Equal("Mehmet", board.OwnerName);
        Assert.NotEqual(Guid.Empty, board.Id);

        var playerId = Guid.NewGuid();
        var entry = new ScoutBoardEntry(board.Id, playerId, "Priority summer guard");
        Assert.Equal(board.Id, entry.BoardId);
        Assert.Equal(playerId, entry.PlayerId);
        Assert.Equal("Priority summer guard", entry.Notes);

        entry.SetNotes("  Confirmed medical clearance  ");
        Assert.Equal("Confirmed medical clearance", entry.Notes);
    }

    [Fact]
    public void GetTeamNameVariants_ResolvesNbaCodesAndFullNamesBidirectionally()
    {
        var fromFull = SleeperScoutingService.GetTeamNameVariants("Dallas Mavericks");
        Assert.Contains("Dallas Mavericks", fromFull);
        Assert.Contains("DAL", fromFull);

        var fromCode = SleeperScoutingService.GetTeamNameVariants("CHI");
        Assert.Contains("CHI", fromCode);
        Assert.Contains("Chicago Bulls", fromCode);
    }
}
