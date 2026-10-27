using System;
using System.Collections.Generic;

namespace SmartScout.Application.DTOs;

public class PlayerComparisonProfileDto
{
    public Guid PlayerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? TeamName { get; set; }
    public string? Position { get; set; }
    public int Season { get; set; }
    public string Archetype { get; set; } = string.Empty;

    public int GamesPlayed { get; set; }
    public decimal? Minutes { get; set; }
    public decimal? Points { get; set; }
    public decimal? Assists { get; set; }
    public decimal? Rebounds { get; set; }
    public decimal? Steals { get; set; }
    public decimal? Blocks { get; set; }
    public decimal? FgPct { get; set; }
    public decimal? Fg3Pct { get; set; }
    public decimal? FtPct { get; set; }
    public decimal? Turnovers { get; set; }

    public decimal? TrueShootingPct { get; set; }
    public decimal? PlayerEfficiencyRating { get; set; }
    public decimal? UsagePct { get; set; }
    public decimal? WinShares { get; set; }
    public decimal? BoxPlusMinus { get; set; }
    public decimal? Pir { get; set; }
    public decimal? FoulsDrawn { get; set; }
    public decimal? PointsPer40 { get; set; }
    public string? Nationality { get; set; }
    public string League { get; set; } = string.Empty;
    public double? EuropeanCompatibilityIndex { get; set; }
    public double? DefensivePressureIndex { get; set; }
    public double? BuyLowBounceBackIndex { get; set; }

    public RadarMetricsDto RadarMetrics { get; set; } = null!;
}

public class PlayerComparisonDto
{
    public PlayerComparisonProfileDto PlayerA { get; set; } = null!;
    public PlayerComparisonProfileDto PlayerB { get; set; } = null!;

    public double SimilarityScore { get; set; } // Cross-similarity between the two players

    // Category Winners ("Scoring" => "PlayerA", "Rebounding" => "PlayerB", etc.)
    public Dictionary<string, string> CategoryWinners { get; set; } = new();

    // Statistical differentials (PlayerA - PlayerB)
    public Dictionary<string, decimal?> Differentials { get; set; } = new();
}
