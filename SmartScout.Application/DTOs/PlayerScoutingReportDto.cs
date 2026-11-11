using System;
using System.Collections.Generic;

namespace SmartScout.Application.DTOs;

public class CareerSeasonStintDto
{
    public int Season { get; set; }
    public string League { get; set; } = string.Empty;
    public string? TeamName { get; set; }
    public int GamesPlayed { get; set; }
    public decimal? Minutes { get; set; }
    public decimal? Points { get; set; }
    public decimal? Rebounds { get; set; }
    public decimal? Assists { get; set; }
    public decimal? Fg3Pct { get; set; }
    public decimal? TrueShootingPct { get; set; }
    public decimal? PtsPer36 { get; set; }
    public decimal? StocksPer36 { get; set; }
    public decimal? Pir { get; set; }
    public decimal? EuroLeagueTransitionIndex { get; set; }
    public decimal LeagueDifficultyCoefficient { get; set; } = 1.00m;
}

public class PlayerScoutingReportDto
{
    public Guid PlayerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string? Position { get; set; }
    public string? Height { get; set; }
    public int? HeightCm { get; set; }
    public int? WeightPounds { get; set; }
    public int? WeightKg { get; set; }
    public int? WingspanCm { get; set; }
    public int? StandingReachCm { get; set; }
    public int? Age { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? ContractExpirySeason { get; set; }
    public string? EstimatedMarketValueTier { get; set; }
    public string? AgencyName { get; set; }
    public string DominantHand { get; set; } = "Right";
    public string? ScoutNotes { get; set; }
    public bool IsWatchlisted { get; set; }
    public string? TeamName { get; set; }
    public string League { get; set; } = "NBA";
    public string? Nationality { get; set; }
    public int Season { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public string Archetype { get; set; } = string.Empty;
    public RadarMetricsDto RadarMetrics { get; set; } = null!;
    public decimal? EuroLeagueTransitionIndex { get; set; }
    public decimal? StocksPer36 { get; set; }
    public decimal? ThreePointRate { get; set; }

    // Shot Diet, Rim Pressure & FIBA Foul Discipline
    public decimal? FreeThrowRate { get; set; }
    public decimal? FoulsCommittedPer36 { get; set; }
    public decimal? NetFoulDifferential { get; set; }
    public decimal? OffensiveReboundShare { get; set; }
    public double TwoPointScoringSharePct { get; set; }
    public double ThreePointScoringSharePct { get; set; }
    public double FreeThrowScoringSharePct { get; set; }
    public string FibaFoulTroubleRisk { get; set; } = "Low Risk";

    // Position-Adjusted Percentiles (0-99)
    public Dictionary<string, int> Percentiles { get; set; } = new();

    // Multi-Season Career Trajectory
    public List<CareerSeasonStintDto> CareerHistory { get; set; } = new();

    // Per-game core stats
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

    // Advanced Stats
    public decimal? TrueShootingPct { get; set; }
    public decimal? PlayerEfficiencyRating { get; set; }
    public decimal? UsagePct { get; set; }
    public decimal? TotalReboundPct { get; set; }
    public decimal? AssistPct { get; set; }
    public decimal? WinShares { get; set; }
    public decimal? BoxPlusMinus { get; set; }
    public decimal? Pir { get; set; }
    public decimal? PtsPer36 { get; set; }
    public decimal? AssistToTurnoverRatio { get; set; }
    public decimal? FoulsDrawn { get; set; }
    public decimal? PointsPer40 { get; set; }
    public decimal? AssistsPer40 { get; set; }
    public decimal? ReboundsPer40 { get; set; }
    public double? EuropeanCompatibilityIndex { get; set; }
    public double? DefensivePressureIndex { get; set; }
    public double? BuyLowBounceBackIndex { get; set; }

    public string TacticalVerdict { get; set; } = string.Empty;
}
