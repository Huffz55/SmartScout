using System;

namespace SmartScout.Application.DTOs;

public class PlayerScoutingReportDto
{
    public Guid PlayerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string? Position { get; set; }
    public string? Height { get; set; }
    public int? WeightPounds { get; set; }
    public string? TeamName { get; set; }
    public string League { get; set; } = "NBA";
    public string? Nationality { get; set; }
    public int Season { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public string Archetype { get; set; } = string.Empty;
    public RadarMetricsDto RadarMetrics { get; set; } = null!;

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
