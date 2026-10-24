using SmartScout.Application.Services;
using SmartScout.Domain.Entities;
using SmartScout.Domain.Enums;
using System;
using Xunit;

namespace SmartScout.UnitTests;

public class PlayerAnalyticsServiceTests
{
    private readonly PlayerAnalyticsService _analyticsService = new();

    private PlayerSeasonStat CreateStat(
        int season = 2024,
        decimal? pts = 25.0m,
        decimal? ast = 7.0m,
        decimal? reb = 6.0m,
        decimal? stl = 1.2m,
        decimal? blk = 0.5m,
        decimal? fg3Pct = 0.38m,
        decimal? tsPct = 0.60m,
        decimal? usgPct = 28.0m,
        decimal? per = 23.0m,
        decimal? ws = 8.0m,
        decimal? bpm = 5.0m,
        decimal? min = 35.0m)
    {
        return new PlayerSeasonStat(
            playerId: Guid.NewGuid(),
            season: season,
            gamesPlayed: 75,
            minutes: min,
            points: pts,
            assists: ast,
            rebounds: reb,
            steals: stl,
            blocks: blk,
            fgPct: 0.48m,
            fg3Pct: fg3Pct,
            ftPct: 0.85m,
            turnovers: 3.0m,
            trueShootingPct: tsPct,
            playerEfficiencyRating: per,
            usagePct: usgPct,
            totalReboundPct: 10.0m,
            assistPct: 32.0m,
            winShares: ws,
            boxPlusMinus: bpm
        );
    }

    [Fact]
    public void CalculateSimilarity_IdenticalStats_ShouldReturn100Percent()
    {
        var statA = CreateStat();
        var statB = CreateStat();

        double score = _analyticsService.CalculateSimilarity(statA, statB);

        Assert.Equal(100.0, score);
    }

    [Fact]
    public void CalculateSimilarity_SimilarArchetypes_ShouldHaveHigherScoreThanOppositeArchetypes()
    {
        // Two elite playmaking perimeter stars
        var lukaStyle = CreateStat(pts: 32.0m, ast: 9.5m, reb: 9.0m, fg3Pct: 0.37m, usgPct: 35.0m, per: 28.0m, bpm: 9.0m);
        var hardenStyle = CreateStat(pts: 30.0m, ast: 8.8m, reb: 6.5m, fg3Pct: 0.36m, usgPct: 34.0m, per: 27.0m, bpm: 8.5m);

        // Defensive rim protector center
        var gobertStyle = CreateStat(pts: 14.0m, ast: 1.2m, reb: 14.0m, fg3Pct: 0.0m, blk: 2.3m, usgPct: 15.0m, per: 19.0m, bpm: 1.5m);

        double similarityLukaHarden = _analyticsService.CalculateSimilarity(lukaStyle, hardenStyle);
        double similarityLukaGobert = _analyticsService.CalculateSimilarity(lukaStyle, gobertStyle);

        Assert.True(similarityLukaHarden > 80.0, $"Expected >80% similarity between perimeter stars, got {similarityLukaHarden}%");
        Assert.True(similarityLukaGobert < 65.0, $"Expected <65% similarity between guard and center, got {similarityLukaGobert}%");
        Assert.True(similarityLukaHarden > similarityLukaGobert);
    }

    [Theory]
    [InlineData("C", "7-0", 25.0, 9.0, 12.0, 0.8, 1.0, 0.38, 28.0, 8.0, PlayerArchetype.PointCenter)]
    [InlineData("C", "7-1", 12.0, 1.0, 12.5, 0.4, 2.3, 0.0, 14.0, 1.0, PlayerArchetype.PaintAnchor)]
    [InlineData("G", "6-4", 16.0, 2.0, 3.5, 0.8, 0.2, 0.42, 19.0, 1.5, PlayerArchetype.Sharpshooter)]
    [InlineData("G", "6-2", 21.0, 8.5, 4.0, 1.2, 0.2, 0.35, 26.0, 4.0, PlayerArchetype.FloorGeneral)]
    [InlineData("F", "6-8", 26.0, 4.0, 7.5, 1.5, 1.1, 0.36, 29.0, 6.0, PlayerArchetype.TwoWayStar)]
    public void DetermineArchetype_ShouldClassifyCorrectTacticalRole(
        string position, string height, double pts, double ast, double reb, double stl, double blk,
        double fg3, double usg, double bpm, PlayerArchetype expected)
    {
        var stat = CreateStat(
            pts: (decimal)pts,
            ast: (decimal)ast,
            reb: (decimal)reb,
            stl: (decimal)stl,
            blk: (decimal)blk,
            fg3Pct: (decimal)fg3,
            usgPct: (decimal)usg,
            bpm: (decimal)bpm
        );

        var archetype = _analyticsService.DetermineArchetype(stat, position, height);

        Assert.Equal(expected, archetype);
    }

    [Fact]
    public void CalculateRadarMetrics_AllMetricsShouldBeClampedBetween0And100()
    {
        var eliteStat = CreateStat(pts: 40.0m, ast: 15.0m, reb: 18.0m, stl: 3.5m, blk: 4.0m, per: 35.0m, bpm: 15.0m, ws: 20.0m);
        var radar = _analyticsService.CalculateRadarMetrics(eliteStat);

        Assert.InRange(radar.Scoring, 0.0, 100.0);
        Assert.InRange(radar.ShootingEfficiency, 0.0, 100.0);
        Assert.InRange(radar.Playmaking, 0.0, 100.0);
        Assert.InRange(radar.Rebounding, 0.0, 100.0);
        Assert.InRange(radar.Defense, 0.0, 100.0);
        Assert.InRange(radar.OverallImpact, 0.0, 100.0);
    }

    [Fact]
    public void Compare_ShouldCalculateAccurateDifferentialsAndWinners()
    {
        var playerA = new Player(1, "Luka", "Doncic", "G-F", "6-7", 230, "DAL", "NBA");
        var statA = CreateStat(pts: 33.0m, ast: 9.8m, reb: 9.2m);

        var playerB = new Player(2, "Trae", "Young", "G", "6-1", 164, "ATL", "NBA");
        var statB = CreateStat(pts: 26.0m, ast: 10.8m, reb: 3.0m);

        var comparison = _analyticsService.Compare(statA, playerA, statB, playerB);

        Assert.NotNull(comparison);
        Assert.Equal("Luka Doncic", comparison.CategoryWinners["Scoring"]);
        Assert.Equal("Luka Doncic", comparison.CategoryWinners["Rebounding"]);
        Assert.Equal("Trae Young", comparison.CategoryWinners["Playmaking"]);

        Assert.Equal(7.0m, comparison.Differentials["Points"]);
        Assert.Equal(-1.0m, comparison.Differentials["Assists"]);
        Assert.Equal(6.2m, comparison.Differentials["Rebounds"]);
    }
}
