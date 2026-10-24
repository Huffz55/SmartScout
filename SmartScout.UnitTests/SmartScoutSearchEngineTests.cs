using SmartScout.Application.Services;
using SmartScout.Domain.Entities;
using System;
using System.Collections.Generic;
using Xunit;

namespace SmartScout.UnitTests;

public class SmartScoutSearchEngineTests
{
    private readonly SmartScoutSearchEngine _searchEngine;

    public SmartScoutSearchEngineTests()
    {
        var analyticsService = new PlayerAnalyticsService();
        _searchEngine = new SmartScoutSearchEngine(analyticsService);
    }

    private (Player, PlayerSeasonStat) CreatePlayerWithStat(
        string first, string last, string pos, string height,
        decimal pts, decimal ast, decimal reb, decimal stl, decimal blk, decimal fg3, decimal ts, decimal bpm, decimal usg = 20.0m)
    {
        var player = new Player(1, first, last, pos, height, 220, "TEAM", "NBA");
        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2024,
            gamesPlayed: 75,
            minutes: 34m,
            points: pts,
            assists: ast,
            rebounds: reb,
            steals: stl,
            blocks: blk,
            fgPct: 0.48m,
            fg3Pct: fg3,
            ftPct: 0.85m,
            turnovers: 2.5m,
            trueShootingPct: ts,
            playerEfficiencyRating: 22.0m,
            usagePct: usg,
            totalReboundPct: 12.0m,
            assistPct: 25.0m,
            winShares: 8.0m,
            boxPlusMinus: bpm
        );

        // Link navigation property
        player.AddSeasonStat(stat);
        // Using reflection or backing field to set stat.Player
        typeof(PlayerSeasonStat).GetProperty(nameof(PlayerSeasonStat.Player))!
            .SetValue(stat, player);

        return (player, stat);
    }

    [Fact]
    public void Search_PlaymakingCenter_ShouldRankPointCenterTop()
    {
        // Arrange
        var (_, jokic) = CreatePlayerWithStat("Nikola", "Jokić", "C", "6-11", 26m, 9.0m, 12.4m, 1.4m, 0.9m, 0.36m, 0.65m, 11.5m);
        var (_, gobert) = CreatePlayerWithStat("Rudy", "Gobert", "C", "7-1", 14m, 1.3m, 12.9m, 0.7m, 2.1m, 0.0m, 0.68m, 1.8m);
        var (_, curry) = CreatePlayerWithStat("Stephen", "Curry", "G", "6-2", 26m, 5.1m, 4.5m, 0.7m, 0.4m, 0.41m, 0.62m, 4.5m);

        var candidateStats = new List<PlayerSeasonStat> { gobert, curry, jokic };

        // Act
        var results = _searchEngine.Search("playmaking center with elite passing", candidateStats);

        // Assert
        Assert.NotEmpty(results);
        Assert.Equal("Nikola Jokić", results[0].FullName);
        Assert.True(results[0].FitScore > 80.0);
        Assert.Contains(results[0].MatchReasons, r => r.Contains("PointCenter") || r.Contains("Playmaker"));
    }

    [Fact]
    public void Search_Sharpshooter_ShouldRankHigh3PtShootersTop()
    {
        // Arrange
        var (_, sniper) = CreatePlayerWithStat("Duncan", "Robinson", "G-F", "6-7", 14m, 2.8m, 2.5m, 0.7m, 0.2m, 0.42m, 0.62m, 1.2m, 18.0m);
        var (_, slasher) = CreatePlayerWithStat("Russell", "Westbrook", "G", "6-4", 18m, 7.5m, 7.0m, 1.4m, 0.4m, 0.29m, 0.50m, 1.0m, 28.0m);

        var candidateStats = new List<PlayerSeasonStat> { slasher, sniper };

        // Act
        var results = _searchEngine.Search("sharpshooter 3pt floor spacer", candidateStats);

        // Assert
        Assert.NotEmpty(results);
        Assert.Equal("Duncan Robinson", results[0].FullName);
    }

    [Fact]
    public void Search_NumericThreshold_ShouldFilterOutUnderperformingPlayers()
    {
        // Arrange
        var (_, star25Pts) = CreatePlayerWithStat("Jayson", "Tatum", "F", "6-8", 27m, 4.9m, 8.1m, 1.0m, 0.6m, 0.38m, 0.61m, 5.5m);
        var (_, role15Pts) = CreatePlayerWithStat("Mikal", "Bridges", "F", "6-6", 18m, 3.6m, 4.5m, 1.0m, 0.4m, 0.37m, 0.56m, 1.0m);

        var candidateStats = new List<PlayerSeasonStat> { role15Pts, star25Pts };

        // Act
        var results = _searchEngine.Search("25+ pts scorer", candidateStats);

        // Assert
        Assert.Single(results);
        Assert.Equal("Jayson Tatum", results[0].FullName);
    }
}
