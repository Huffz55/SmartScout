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
}
