using System;
using System.Collections.Generic;

namespace SmartScout.Application.DTOs;

public class SleeperPlayerDto
{
    public Guid PlayerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public string? Nationality { get; set; }
    public int? Age { get; set; }
    public int Season { get; set; }
    public string League { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public decimal LeagueDifficultyCoefficient { get; set; }

    // Sleeper Classification: "ForgottenPeak", "LowMinuteMonster", "StepUpCandidate"
    public string SleeperCategory { get; set; } = string.Empty;
    public double SleeperScore { get; set; }
    public decimal DifficultyAdjustedGameScore { get; set; }

    // Traditional & Per-36 / Per-40 Production
    public int GamesPlayed { get; set; }
    public decimal? MinutesPerGame { get; set; }
    public decimal? Points { get; set; }
    public decimal? Rebounds { get; set; }
    public decimal? Assists { get; set; }
    public decimal? PtsPer36 { get; set; }
    public decimal? RebPer36 { get; set; }
    public decimal? AstPer36 { get; set; }
    public decimal? StlPer36 { get; set; }
    public decimal? BlkPer36 { get; set; }
    public decimal? TovPer36 { get; set; }
    public decimal? PointsPer40 { get; set; }

    // Efficiency & Advanced Metrics
    public decimal? TrueShootingPct { get; set; }
    public decimal? EffectiveFgPct { get; set; }
    public decimal? Fg3Pct { get; set; }
    public decimal? ThreePointsAttempted { get; set; }
    public decimal? UsagePct { get; set; }
    public decimal? AssistToTurnoverRatio { get; set; }
    public decimal? GameScoreAverage { get; set; }
    public decimal? Pir { get; set; }

    // Context & Scouting Insights
    public string? PeakSeasonContext { get; set; }
    public List<string> ScoutingReasons { get; set; } = new();
}
