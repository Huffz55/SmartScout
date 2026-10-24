using System;

namespace SmartScout.Application.DTOs;

public class SimilarPlayerDto
{
    public Guid PlayerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string? TeamName { get; set; }
    public string? Position { get; set; }
    public int Season { get; set; }
    public double SimilarityScore { get; set; } // e.g. 94.5%
    public string Archetype { get; set; } = string.Empty;

    // Per-game core stats for quick comparison
    public int GamesPlayed { get; set; }
    public decimal? Minutes { get; set; }
    public decimal? Points { get; set; }
    public decimal? Assists { get; set; }
    public decimal? Rebounds { get; set; }
    public decimal? Steals { get; set; }
    public decimal? Blocks { get; set; }

    // Advanced & European Stats
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

    // 0-100 Radar representation
    public RadarMetricsDto RadarMetrics { get; set; } = null!;
}
