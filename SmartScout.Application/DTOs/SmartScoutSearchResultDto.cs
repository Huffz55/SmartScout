using System;
using System.Collections.Generic;

namespace SmartScout.Application.DTOs;

public class SmartScoutSearchResultDto
{
    public Guid PlayerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string? TeamName { get; set; }
    public string? Position { get; set; }
    public int Season { get; set; }
    public string Archetype { get; set; } = string.Empty;

    // How strongly the player matches the natural language scouting prompt (0-100%)
    public double FitScore { get; set; }

    // Human-readable tactical explanations of why this player matched
    public List<string> MatchReasons { get; set; } = new();

    // Key Stats
    public decimal? Points { get; set; }
    public decimal? Rebounds { get; set; }
    public decimal? Assists { get; set; }
    public decimal? Steals { get; set; }
    public decimal? Blocks { get; set; }
    public decimal? PtsPer36 { get; set; }
    public decimal? RebPer36 { get; set; }
    public decimal? AstPer36 { get; set; }
    public decimal? TrueShootingPct { get; set; }
    public decimal? EffectiveFgPct { get; set; }
    public decimal? AssistToTurnoverRatio { get; set; }
    public decimal? GameScoreAverage { get; set; }
    public decimal? PlayerEfficiencyRating { get; set; }
    public decimal? UsagePct { get; set; }
    public decimal? WinShares { get; set; }
    public decimal? BoxPlusMinus { get; set; }
    public decimal? Pir { get; set; }
    public decimal? FoulsDrawn { get; set; }
    public decimal? PointsPer40 { get; set; }
    public string? Nationality { get; set; }
    public string League { get; set; } = string.Empty;
    public decimal LeagueDifficultyCoefficient { get; set; } = 1.00m;
    public double? EuropeanCompatibilityIndex { get; set; }
    public double? DefensivePressureIndex { get; set; }
    public double? BuyLowBounceBackIndex { get; set; }

    public RadarMetricsDto RadarMetrics { get; set; } = null!;
}
