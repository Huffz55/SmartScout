using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using SmartScout.Domain.Enums;
using System;
using System.Collections.Generic;

namespace SmartScout.Application.Services;

public class PlayerAnalyticsService : IPlayerAnalyticsService
{
    private record FeatureDefinition(
        Func<PlayerSeasonStat, decimal?> Selector, 
        double Min, 
        double Max, 
        double Weight);

    private static readonly FeatureDefinition[] FeatureVector =
    {
        new(s => s.Points, 0.0, 35.0, 1.2),
        new(s => s.Assists, 0.0, 12.0, 1.1),
        new(s => s.Rebounds, 0.0, 15.0, 1.0),
        new(s => s.Steals, 0.0, 2.5, 0.8),
        new(s => s.Blocks, 0.0, 3.5, 0.8),
        new(s => s.Fg3Pct, 0.0, 0.45, 0.9),
        new(s => s.TrueShootingPct, 0.40, 0.70, 1.3),
        new(s => s.UsagePct, 10.0, 40.0, 1.2),
        new(s => s.PlayerEfficiencyRating, 5.0, 32.0, 1.4),
        new(s => s.BoxPlusMinus, -8.0, 12.0, 1.2),
        new(s => s.WinShares, -1.0, 16.0, 1.0),
        new(s => s.Minutes, 5.0, 40.0, 0.6)
    };

    public double CalculateSimilarity(PlayerSeasonStat target, PlayerSeasonStat candidate)
    {
        double sumWeightedSqDiff = 0.0;
        double totalWeight = 0.0;

        foreach (var feature in FeatureVector)
        {
            double valTarget = Normalize(feature.Selector(target), feature.Min, feature.Max);
            double valCandidate = Normalize(feature.Selector(candidate), feature.Min, feature.Max);

            double diff = valTarget - valCandidate;
            sumWeightedSqDiff += feature.Weight * (diff * diff);
            totalWeight += feature.Weight;
        }

        double normalizedDistance = Math.Sqrt(sumWeightedSqDiff / totalWeight);
        double similarityScore = Math.Max(0.0, Math.Min(100.0, (1.0 - normalizedDistance) * 100.0));

        return Math.Round(similarityScore, 1);
    }

    public PlayerArchetype DetermineArchetype(PlayerSeasonStat stat, string? position = null, string? height = null)
    {
        decimal pts = stat.Points ?? 0;
        decimal reb = stat.Rebounds ?? 0;
        decimal ast = stat.Assists ?? 0;
        decimal stl = stat.Steals ?? 0;
        decimal blk = stat.Blocks ?? 0;
        decimal fg3 = stat.Fg3Pct ?? 0;
        decimal usg = stat.UsagePct ?? 20;
        decimal bpm = stat.BoxPlusMinus ?? 0;

        bool isBig = (!string.IsNullOrEmpty(position) && (position.Contains('C') || position.Contains('F')))
                     || (!string.IsNullOrEmpty(height) && (height.StartsWith("6-1") || height.StartsWith("7-")));

        // 1. Point Center: High rebounding + elite playmaking big
        if (isBig && ast >= 6.0m && reb >= 8.0m)
            return PlayerArchetype.PointCenter;

        // 2. Two-Way Star: Elite scoring, strong impact, plus defensive disruption
        if (pts >= 22.0m && bpm >= 3.5m && (stl >= 1.2m || blk >= 0.9m))
            return PlayerArchetype.TwoWayStar;

        // 3. Paint Anchor: Rim protector with high blocks & rebounds, lower usage
        if (isBig && blk >= 1.6m && reb >= 8.5m && pts <= 17.0m)
            return PlayerArchetype.PaintAnchor;

        // 4. Stretch Big: High rebounds, solid 3PT shooting
        if (isBig && fg3 >= 0.35m && reb >= 5.5m)
            return PlayerArchetype.StretchBig;

        // 5. Sharpshooter: High 3-point percentage with lower playmaking
        if (fg3 >= 0.38m && ast < 3.5m && usg <= 25.0m)
            return PlayerArchetype.Sharpshooter;

        // 6. Floor General: Elite assist volume and engine
        if (ast >= 6.5m)
            return PlayerArchetype.FloorGeneral;

        // 7. Slashing Wing: High scoring & usage on perimeter
        if (pts >= 19.0m && usg >= 25.0m)
            return PlayerArchetype.SlashingWing;

        return PlayerArchetype.RolePlayer;
    }

    public RadarMetricsDto CalculateRadarMetrics(PlayerSeasonStat stat)
    {
        // 0-100 normalized radar metrics
        double scoring = Clamp100((((double)(stat.Points ?? 0) / 32.0) * 0.7 + ((double)(stat.UsagePct ?? 20) / 38.0) * 0.3) * 100);
        
        double ts = (double)(stat.TrueShootingPct ?? 0.54m);
        double efficiency = Clamp100(((ts - 0.45) / (0.68 - 0.45)) * 100);

        double playmaking = Clamp100((((double)(stat.Assists ?? 0) / 10.5) * 0.7 + ((double)(stat.AssistPct ?? ((stat.Assists ?? 0) * 3m)) / 45.0) * 0.3) * 100);

        double rebounding = Clamp100((((double)(stat.Rebounds ?? 0) / 13.0) * 0.6 + ((double)(stat.TotalReboundPct ?? ((stat.Rebounds ?? 0) * 1.5m)) / 22.0) * 0.4) * 100);

        double defense = Clamp100((((double)(stat.Steals ?? 0) / 2.2) * 0.5 + ((double)(stat.Blocks ?? 0) / 2.5) * 0.5) * 100);

        double per = (double)(stat.PlayerEfficiencyRating ?? 15.0m);
        double bpm = (double)(stat.BoxPlusMinus ?? 0.0m);
        double ws = (double)(stat.WinShares ?? 3.0m);
        double impact = Clamp100((((per - 8.0) / 22.0) * 0.4 + ((bpm + 4.0) / 14.0) * 0.3 + (ws / 14.0) * 0.3) * 100);

        return new RadarMetricsDto(
            Math.Round(scoring, 1),
            Math.Round(efficiency, 1),
            Math.Round(playmaking, 1),
            Math.Round(rebounding, 1),
            Math.Round(defense, 1),
            Math.Round(impact, 1));
    }

    public PlayerComparisonDto Compare(PlayerSeasonStat statA, Player playerA, PlayerSeasonStat statB, Player playerB)
    {
        var profileA = CreateProfile(statA, playerA);
        var profileB = CreateProfile(statB, playerB);
        double similarity = CalculateSimilarity(statA, statB);

        var categoryWinners = new Dictionary<string, string>
        {
            ["Scoring"] = (profileA.RadarMetrics.Scoring >= profileB.RadarMetrics.Scoring) ? profileA.FullName : profileB.FullName,
            ["ShootingEfficiency"] = (profileA.RadarMetrics.ShootingEfficiency >= profileB.RadarMetrics.ShootingEfficiency) ? profileA.FullName : profileB.FullName,
            ["Playmaking"] = (profileA.RadarMetrics.Playmaking >= profileB.RadarMetrics.Playmaking) ? profileA.FullName : profileB.FullName,
            ["Rebounding"] = (profileA.RadarMetrics.Rebounding >= profileB.RadarMetrics.Rebounding) ? profileA.FullName : profileB.FullName,
            ["Defense"] = (profileA.RadarMetrics.Defense >= profileB.RadarMetrics.Defense) ? profileA.FullName : profileB.FullName,
            ["OverallImpact"] = (profileA.RadarMetrics.OverallImpact >= profileB.RadarMetrics.OverallImpact) ? profileA.FullName : profileB.FullName
        };

        var differentials = new Dictionary<string, decimal?>
        {
            ["Points"] = (statA.Points ?? 0) - (statB.Points ?? 0),
            ["Assists"] = (statA.Assists ?? 0) - (statB.Assists ?? 0),
            ["Rebounds"] = (statA.Rebounds ?? 0) - (statB.Rebounds ?? 0),
            ["Steals"] = (statA.Steals ?? 0) - (statB.Steals ?? 0),
            ["Blocks"] = (statA.Blocks ?? 0) - (statB.Blocks ?? 0),
            ["TrueShootingPct"] = (statA.TrueShootingPct ?? 0) - (statB.TrueShootingPct ?? 0),
            ["PER"] = (statA.PlayerEfficiencyRating ?? 0) - (statB.PlayerEfficiencyRating ?? 0),
            ["WinShares"] = (statA.WinShares ?? 0) - (statB.WinShares ?? 0),
            ["BPM"] = (statA.BoxPlusMinus ?? 0) - (statB.BoxPlusMinus ?? 0)
        };

        return new PlayerComparisonDto
        {
            PlayerA = profileA,
            PlayerB = profileB,
            SimilarityScore = similarity,
            CategoryWinners = categoryWinners,
            Differentials = differentials
        };
    }

    private PlayerComparisonProfileDto CreateProfile(PlayerSeasonStat stat, Player player)
    {
        return new PlayerComparisonProfileDto
        {
            PlayerId = player.Id,
            FullName = $"{player.FirstName} {player.LastName}".Trim(),
            TeamName = player.TeamName,
            Position = player.Position,
            Season = stat.Season,
            Archetype = DetermineArchetype(stat, player.Position, player.Height).ToString(),
            GamesPlayed = stat.GamesPlayed,
            Minutes = stat.Minutes,
            Points = stat.Points,
            Assists = stat.Assists,
            Rebounds = stat.Rebounds,
            Steals = stat.Steals,
            Blocks = stat.Blocks,
            FgPct = stat.FgPct,
            Fg3Pct = stat.Fg3Pct,
            FtPct = stat.FtPct,
            Turnovers = stat.Turnovers,
            TrueShootingPct = stat.TrueShootingPct,
            PlayerEfficiencyRating = stat.PlayerEfficiencyRating,
            UsagePct = stat.UsagePct,
            WinShares = stat.WinShares,
            BoxPlusMinus = stat.BoxPlusMinus,
            Pir = stat.Pir,
            FoulsDrawn = stat.FoulsDrawn,
            PointsPer40 = stat.PointsPer40,
            Nationality = player.Nationality,
            League = player.League,
            EuropeanCompatibilityIndex = CalculateEuropeanCompatibilityIndex(stat),
            DefensivePressureIndex = CalculateDefensivePressureIndex(stat),
            RadarMetrics = CalculateRadarMetrics(stat)
        };
    }

    public double CalculateEuropeanCompatibilityIndex(PlayerSeasonStat stat)
    {
        // 1. Perimeter Spacing (3P%): FIBA defenses pack the paint; high 3P% is critical
        double fg3 = (double)(stat.Fg3Pct ?? 0.30m);
        double spacingScore = Clamp100(((fg3 - 0.25) / (0.42 - 0.25)) * 100);

        // 2. Free Throw Reliability: punishing fouls & closing tight 40-min games
        double ft = (double)(stat.FtPct ?? 0.70m);
        double ftScore = Clamp100(((ft - 0.60) / (0.88 - 0.60)) * 100);

        // 3. Ball Security / Low Turnovers: fewer possessions per game in Europe make turnovers costlier
        double tov = (double)(stat.Turnovers ?? 2.0m);
        double ballSecurityScore = Clamp100(((3.8 - tov) / (3.8 - 1.2)) * 100);

        // 4. Drawing Contact / Fouls: breaking through packed European defensive walls
        double fouls = (double)(stat.FoulsDrawn ?? ((stat.FtPct.HasValue && stat.Points.HasValue) ? (stat.Points.Value * 0.2m) : 2.5m));
        double foulDrawScore = Clamp100(((fouls - 1.0) / (5.0 - 1.0)) * 100);

        // 5. Efficiency Foundation (PIR or True Shooting)
        double effScore;
        if (stat.Pir.HasValue)
        {
            effScore = Clamp100((((double)stat.Pir.Value - 4.0) / (18.0 - 4.0)) * 100);
        }
        else
        {
            double ts = (double)(stat.TrueShootingPct ?? 0.52m);
            effScore = Clamp100(((ts - 0.46) / (0.64 - 0.46)) * 100);
        }

        double eci = (spacingScore * 0.30) + 
                     (ballSecurityScore * 0.25) + 
                     (ftScore * 0.20) + 
                     (foulDrawScore * 0.15) + 
                     (effScore * 0.10);

        return Math.Round(Clamp100(eci), 1);
    }

    public double CalculateDefensivePressureIndex(PlayerSeasonStat stat)
    {
        // Dušan Alimpijević / Beşiktaş system: full-court harassment, active hands, disruptive perimeter pressure
        // 1. Steals & Passing Lane Disruption (35%)
        double stl = (double)(stat.Steals ?? 0.6m);
        double stealScore = Clamp100(((stl - 0.4) / (1.8 - 0.4)) * 100);

        // 2. Defensive Impact / BPM (25%)
        double bpm = (double)(stat.BoxPlusMinus ?? 0.0m);
        double impactScore = Clamp100(((bpm + 3.0) / 7.0) * 100);

        // 3. Physicality & Drawing Contact / Taking Charges (20%)
        double fouls = (double)(stat.FoulsDrawn ?? 2.0m);
        double contactScore = Clamp100(((fouls - 1.0) / (4.5 - 1.0)) * 100);

        // 4. Activity & Motor: Rim contests & defensive rebounding (20%)
        double blk = (double)(stat.Blocks ?? 0.3m);
        double reb = (double)(stat.Rebounds ?? 2.5m);
        double motorScore = Clamp100((((blk / 1.5) * 0.5) + ((reb / 7.0) * 0.5)) * 100);

        double dpi = (stealScore * 0.35) + 
                     (impactScore * 0.25) + 
                     (contactScore * 0.20) + 
                     (motorScore * 0.20);

        return Math.Round(Clamp100(dpi), 1);
    }

    private static double Normalize(decimal? value, double min, double max)
    {
        if (!value.HasValue) return 0.5;
        double val = (double)value.Value;
        if (val <= min) return 0.0;
        if (val >= max) return 1.0;
        return (val - min) / (max - min);
    }

    private static double Clamp100(double value) => Math.Max(0.0, Math.Min(100.0, value));
}
