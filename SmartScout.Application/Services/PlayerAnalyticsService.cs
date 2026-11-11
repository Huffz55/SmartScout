using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using SmartScout.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

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
        new(s => s.PtsPer36 ?? (s.Minutes > 0 ? (s.Points / s.Minutes) * 36m : null), 4.0, 32.0, 1.4),
        new(s => s.AstPer36 ?? (s.Minutes > 0 ? (s.Assists / s.Minutes) * 36m : null), 0.5, 12.0, 1.3),
        new(s => s.RebPer36 ?? (s.Minutes > 0 ? (s.Rebounds / s.Minutes) * 36m : null), 1.0, 16.0, 1.2),
        new(s => s.StocksPer36 ?? ((s.StlPer36 ?? 0m) + (s.BlkPer36 ?? 0m)), 0.2, 5.0, 1.1),
        new(s => s.TrueShootingPct, 0.42, 0.68, 1.5),
        new(s => s.ThreePointRate, 0.05, 0.70, 1.0),
        new(s => s.Fg3Pct, 0.20, 0.45, 0.9),
        new(s => s.AssistToTurnoverRatio, 0.6, 3.5, 1.2),
        new(s => s.GameScoreAverage, 2.0, 22.0, 1.3),
        new(s => s.UsagePct, 12.0, 36.0, 1.2),
        new(s => s.Pir, 2.0, 28.0, 1.1)
    };

    public double CalculateSimilarity(PlayerSeasonStat target, PlayerSeasonStat candidate)
    {
        double sumWeightedSqDiff = 0.0;
        double totalWeight = 0.0;

        foreach (var feature in FeatureVector)
        {
            var rawTarget = feature.Selector(target);
            var rawCandidate = feature.Selector(candidate);

            // Skip metric if missing on either side to maintain pure fair cross-league comparison
            if (!rawTarget.HasValue || !rawCandidate.HasValue)
                continue;

            double valTarget = Normalize(rawTarget, feature.Min, feature.Max);
            double valCandidate = Normalize(rawCandidate, feature.Min, feature.Max);

            double diff = valTarget - valCandidate;
            sumWeightedSqDiff += feature.Weight * (diff * diff);
            totalWeight += feature.Weight;
        }

        if (totalWeight <= 0.0) return 50.0;

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

        // 7. SlashingWing: High scoring & usage on perimeter
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

        double per = (double)(stat.PlayerEfficiencyRating ?? (stat.Pir ?? 15.0m));
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
            ["BPM"] = (statA.BoxPlusMinus ?? 0) - (statB.BoxPlusMinus ?? 0),
            ["PtsPer36"] = (profileA.PtsPer36 ?? 0) - (profileB.PtsPer36 ?? 0),
            ["AstPer36"] = (profileA.AstPer36 ?? 0) - (profileB.AstPer36 ?? 0),
            ["RebPer36"] = (profileA.RebPer36 ?? 0) - (profileB.RebPer36 ?? 0),
            ["StocksPer36"] = (profileA.StocksPer36 ?? 0) - (profileB.StocksPer36 ?? 0),
            ["Pir"] = (statA.Pir ?? 0) - (statB.Pir ?? 0),
            ["ETI"] = (profileA.EuroLeagueTransitionIndex ?? 0) - (profileB.EuroLeagueTransitionIndex ?? 0)
        };

        int winsA = categoryWinners.Values.Count(v => v == profileA.FullName);
        int winsB = categoryWinners.Values.Count(v => v == profileB.FullName);
        string overallLeader = winsA >= winsB ? profileA.FullName : profileB.FullName;
        string verdict = $"{overallLeader} leads in {Math.Max(winsA, winsB)} of 6 core scouting dimensions (Profile Similarity: {similarity:0.0}%). " +
                         $"{profileA.FullName} profiles as a {profileA.Archetype} ({profileA.EuroLeagueTransitionIndex ?? 65m:0.0} ETI) while {profileB.FullName} operates as a {profileB.Archetype} ({profileB.EuroLeagueTransitionIndex ?? 65m:0.0} ETI).";

        return new PlayerComparisonDto
        {
            PlayerA = profileA,
            PlayerB = profileB,
            SimilarityScore = similarity,
            CategoryWinners = categoryWinners,
            Differentials = differentials,
            ScoutingVerdict = verdict
        };
    }

    private PlayerComparisonProfileDto CreateProfile(PlayerSeasonStat stat, Player player)
    {
        decimal min = stat.Minutes ?? 0m;
        decimal? pts36 = stat.PtsPer36 ?? (min > 0 && stat.Points.HasValue ? Math.Round((stat.Points.Value / min) * 36m, 1) : null);
        decimal? ast36 = stat.AstPer36 ?? (min > 0 && stat.Assists.HasValue ? Math.Round((stat.Assists.Value / min) * 36m, 1) : null);
        decimal? reb36 = stat.RebPer36 ?? (min > 0 && stat.Rebounds.HasValue ? Math.Round((stat.Rebounds.Value / min) * 36m, 1) : null);
        decimal? stocks36 = stat.StocksPer36 ?? (min > 0 ? Math.Round((((stat.Steals ?? 0m) + (stat.Blocks ?? 0m)) / min) * 36m, 2) : null);

        return new PlayerComparisonProfileDto
        {
            PlayerId = player.Id,
            FullName = $"{player.FirstName} {player.LastName}".Trim(),
            TeamName = stat.TeamName ?? player.TeamName,
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
            League = !string.IsNullOrWhiteSpace(stat.League) ? stat.League : player.League,
            EuropeanCompatibilityIndex = CalculateEuropeanCompatibilityIndex(stat),
            DefensivePressureIndex = CalculateDefensivePressureIndex(stat),
            BuyLowBounceBackIndex = CalculateBuyLowBounceBackIndex(stat, player.SeasonStats),
            Age = player.CurrentAge,
            HeightCm = player.HeightCm,
            WeightKg = player.WeightKg,
            WingspanCm = player.WingspanCm,
            StandingReachCm = player.StandingReachCm,
            ContractExpirySeason = player.ContractExpirySeason,
            EstimatedMarketValueTier = player.EstimatedMarketValueTier,
            PtsPer36 = pts36,
            AstPer36 = ast36,
            RebPer36 = reb36,
            StocksPer36 = stocks36,
            ThreePointRate = stat.ThreePointRate,
            AssistToTurnoverRatio = stat.AssistToTurnoverRatio,
            EuroLeagueTransitionIndex = stat.EuroLeagueTransitionIndex,
            Percentiles = CalculatePositionalPercentiles(stat, player.Position),
            RadarMetrics = CalculateRadarMetrics(stat)
        };
    }

    public Dictionary<string, int> CalculatePositionalPercentiles(PlayerSeasonStat stat, string? position = null)
    {
        string pos = (position ?? stat.Player?.Position ?? "G").ToUpperInvariant();
        bool isCenter = pos.Contains('C');
        bool isForward = !isCenter && pos.Contains('F');

        decimal min = stat.Minutes ?? 20m;
        double pts36 = (double)(stat.PtsPer36 ?? (min > 0 ? ((stat.Points ?? 0m) / min) * 36m : 12m));
        double ast36 = (double)(stat.AstPer36 ?? (min > 0 ? ((stat.Assists ?? 0m) / min) * 36m : 2.5m));
        double reb36 = (double)(stat.RebPer36 ?? (min > 0 ? ((stat.Rebounds ?? 0m) / min) * 36m : 4.5m));
        double stocks36 = (double)(stat.StocksPer36 ?? ((stat.StlPer36 ?? 0.8m) + (stat.BlkPer36 ?? 0.4m)));
        double ts = (double)(stat.TrueShootingPct ?? 0.54m);
        double fg3 = (double)(stat.Fg3Pct ?? 0.33m);
        double astTov = (double)(stat.AssistToTurnoverRatio ?? 1.4m);
        double tpr = (double)(stat.ThreePointRate ?? 0.32m);

        decimal fga = (stat.TwoPointsAttempted ?? 0m) + (stat.ThreePointsAttempted ?? 0m);
        double ftr = fga > 0 && stat.FreeThrowsAttempted.HasValue
            ? (double)(stat.FreeThrowsAttempted.Value / fga)
            : (double)((stat.FoulsDrawn ?? 2.2m) / 10m);

        double pf36 = min > 0 && stat.PersonalFoulsCommitted.HasValue
            ? (double)((stat.PersonalFoulsCommitted.Value / min) * 36m)
            : 3.2;

        // Positional calibration bounds (min, max for 1st to 99th percentile)
        (double minAst, double maxAst) = isCenter ? (0.5, 5.2) : isForward ? (1.0, 6.8) : (2.0, 9.8);
        (double minReb, double maxReb) = isCenter ? (5.0, 14.5) : isForward ? (3.2, 11.2) : (1.5, 6.8);
        (double minStk, double maxStk) = isCenter ? (0.8, 4.2) : isForward ? (0.7, 3.4) : (0.6, 2.9);
        (double min3P, double max3P) = isCenter ? (0.20, 0.40) : isForward ? (0.26, 0.43) : (0.28, 0.45);
        (double minTpr, double maxTpr) = isCenter ? (0.02, 0.45) : isForward ? (0.15, 0.62) : (0.22, 0.70);

        return new Dictionary<string, int>
        {
            ["Points"] = ToPercentile((double)(stat.Points ?? 0m), 4.0, 22.0),
            ["PtsPer36"] = ToPercentile(pts36, 7.0, 26.5),
            ["TrueShootingPct"] = ToPercentile(ts, 0.46, 0.66),
            ["Fg3Pct"] = ToPercentile(fg3, min3P, max3P),
            ["AstPer36"] = ToPercentile(ast36, minAst, maxAst),
            ["RebPer36"] = ToPercentile(reb36, minReb, maxReb),
            ["StocksPer36"] = ToPercentile(stocks36, minStk, maxStk),
            ["AssistToTurnoverRatio"] = ToPercentile(astTov, 0.7, 3.4),
            ["ThreePointRate"] = ToPercentile(tpr, minTpr, maxTpr),
            ["FreeThrowRate"] = ToPercentile(ftr, 0.10, 0.52),
            ["FoulDiscipline"] = ToPercentile(5.5 - pf36, 0.5, 3.8),
            ["Pir"] = ToPercentile((double)(stat.Pir ?? stat.GameScoreAverage ?? 8m), 2.0, 21.0),
            ["EuroLeagueTransitionIndex"] = ToPercentile((double)(stat.EuroLeagueTransitionIndex ?? 60m), 42.0, 92.0)
        };
    }

    public void PopulateShotDietAndPhysicality(PlayerScoutingReportDto dto, PlayerSeasonStat stat)
    {
        decimal min = stat.Minutes ?? 0m;
        decimal twoPa = stat.TwoPointsAttempted ?? 0m;
        decimal threePa = stat.ThreePointsAttempted ?? 0m;
        decimal fga = twoPa + threePa;
        decimal fta = stat.FreeThrowsAttempted ?? 0m;

        if (fga > 0 && fta > 0)
        {
            dto.FreeThrowRate = Math.Round(fta / fga, 3);
        }
        else if (stat.FoulsDrawn.HasValue && stat.Points.HasValue && stat.Points.Value > 0)
        {
            dto.FreeThrowRate = Math.Round(Math.Clamp((stat.FoulsDrawn.Value * 1.4m) / Math.Max(4m, stat.Points.Value), 0.08m, 0.65m), 3);
        }

        if (min > 0 && stat.PersonalFoulsCommitted.HasValue)
        {
            dto.FoulsCommittedPer36 = Math.Round((stat.PersonalFoulsCommitted.Value / min) * 36m, 2);
        }

        if (stat.FoulsDrawn.HasValue && stat.PersonalFoulsCommitted.HasValue)
        {
            dto.NetFoulDifferential = Math.Round(stat.FoulsDrawn.Value - stat.PersonalFoulsCommitted.Value, 2);
        }

        if (stat.OffensiveRebounds.HasValue && stat.Rebounds.HasValue && stat.Rebounds.Value > 0)
        {
            dto.OffensiveReboundShare = Math.Round(Math.Clamp(stat.OffensiveRebounds.Value / stat.Rebounds.Value, 0m, 1m), 3);
        }

        // Scoring Split (2PT vs 3PT vs FT points share)
        decimal pts2 = (stat.TwoPointsMade ?? 0m) * 2m;
        decimal pts3 = (stat.ThreePointsMade ?? 0m) * 3m;
        decimal ptsFt = stat.FreeThrowsMade ?? 0m;
        decimal totalCalcPts = pts2 + pts3 + ptsFt;

        if (totalCalcPts > 0)
        {
            dto.TwoPointScoringSharePct = Math.Round((double)(pts2 / totalCalcPts) * 100.0, 1);
            dto.ThreePointScoringSharePct = Math.Round((double)(pts3 / totalCalcPts) * 100.0, 1);
            dto.FreeThrowScoringSharePct = Math.Round(Math.Max(0.0, 100.0 - dto.TwoPointScoringSharePct - dto.ThreePointScoringSharePct), 1);
        }
        else
        {
            double tpr = (double)(stat.ThreePointRate ?? 0.35m);
            double ftShare = 16.0;
            double threeShare = Math.Round(Math.Clamp(tpr * 85.0, 5.0, 68.0), 1);
            double twoShare = Math.Round(100.0 - threeShare - ftShare, 1);
            dto.TwoPointScoringSharePct = twoShare;
            dto.ThreePointScoringSharePct = threeShare;
            dto.FreeThrowScoringSharePct = ftShare;
        }

        decimal pf36 = dto.FoulsCommittedPer36 ?? 3.0m;
        dto.FibaFoulTroubleRisk = pf36 >= 4.3m
            ? "High Foul Risk"
            : pf36 >= 3.4m
                ? "Moderate"
                : "Low Risk";
    }

    private static int ToPercentile(double value, double min, double max)
    {
        if (max <= min) return 50;
        double ratio = (value - min) / (max - min);
        return (int)Math.Round(Math.Clamp(ratio * 98.0 + 1.0, 1.0, 99.0));
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

    public double CalculateBuyLowBounceBackIndex(PlayerSeasonStat currentStat, IEnumerable<PlayerSeasonStat>? careerHistory = null)
    {
        // Moneyball "Uyuyan Dev / Buy-Low / Bounce-Back" Index (0-100):
        // Identifies players who either:
        // A) Had a high-level prior peak season (e.g., in EuroCup/NBA/EuroLeague) and are currently in a reduced role / dip, OR
        // B) Are currently playing limited bench minutes (<= 22 MPG) but posting elite Per-40 & shooting efficiency numbers.

        double curPts = (double)(currentStat.Points ?? 0m);
        double curMin = (double)(currentStat.Minutes ?? 20m);
        double ptsPer40 = Math.Min(35.0, (double)(currentStat.PointsPer40 ?? (curMin > 0 ? (decimal)(curPts / curMin * 40.0) : 0m)));
        double astPer40 = Math.Min(12.0, (double)(currentStat.AssistsPer40 ?? (curMin > 0 ? (decimal)((double)(currentStat.Assists ?? 0m) / curMin * 40.0) : 0m)));
        double ts = Math.Min(0.75, Math.Max(0.0, (double)(currentStat.TrueShootingPct ?? 0.54m)));
        double fg3 = Math.Min(0.55, Math.Max(0.0, (double)(currentStat.Fg3Pct ?? 0.33m)));

        // Sample & minute reliability shrinkage: prevent 3-MPG / 4-GP garbage-time noise from hitting 100
        double minuteReliability = curMin >= 10.0 ? 1.0 : Math.Max(0.25, curMin / 10.0);
        double gpReliability = currentStat.GamesPlayed >= 10 ? 1.0 : Math.Max(0.45, currentStat.GamesPlayed / 10.0);
        double reliability = minuteReliability * (0.40 + 0.60 * gpReliability);

        // Signal B: Per-40 Underutilized Gem Score (high per-minute efficiency in limited/moderate playing time)
        double per40OffenseScore = Clamp100(((ptsPer40 + (astPer40 * 1.2) - 12.0) / (30.0 - 12.0)) * 100.0);
        double efficiencyRetainedScore = Clamp100((((ts - 0.48) / 0.16) * 0.6 + ((fg3 - 0.28) / 0.14) * 0.4) * 100.0);
        double roleDiscountFactor = curMin <= 22.0
            ? Clamp100(((26.0 - curMin) / 14.0) * 100.0)
            : curMin < 28.0
                ? Clamp100(((28.0 - curMin) / 10.0) * 35.0)
                : 0.0;

        double underutilizedGemScore = ((per40OffenseScore * 0.45) + (efficiencyRetainedScore * 0.30) + (roleDiscountFactor * 0.25)) * reliability;

        // Signal A: Multi-Season Career Peak vs Current Dip (recent 3-year window prior to current season)
        double multiSeasonBounceBackScore = 0.0;
        var historyList = (careerHistory ?? currentStat.Player?.SeasonStats)?
            .Where(s => s.Season < currentStat.Season && s.Season >= currentStat.Season - 3 && s.GamesPlayed >= 5)
            .ToList();

        if (historyList != null && historyList.Any())
        {
            double peakPts = (double)historyList.Max(s => s.Points ?? 0m);
            double peakPirOrPer = (double)historyList.Max(s => s.Pir ?? s.PlayerEfficiencyRating ?? 0m);
            double peakMin = (double)historyList.Max(s => s.Minutes ?? 0m);

            double ptsDrop = Math.Max(0.0, peakPts - curPts);
            double minDrop = Math.Max(0.0, peakMin - curMin);

            if (peakPts >= 10.0 || peakPirOrPer >= 11.0)
            {
                // 1. Proven Ceiling (40%): How good was the player at their peak?
                double ceilingScore = Clamp100((((peakPts - 8.0) / 14.0) * 0.6 + ((peakPirOrPer - 8.0) / 14.0) * 0.4) * 100.0);

                // 2. Buy-Low Discount / Role Reduction (35%): How much did their raw volume/minutes drop?
                double discountScore = Clamp100((((ptsDrop / 8.0) * 0.6) + ((minDrop / 10.0) * 0.4)) * 100.0);

                // 3. Underlying Skill Retention (25%): Per-40 output & shooting efficiency still intact
                double skillIntactScore = (per40OffenseScore * 0.5) + (efficiencyRetainedScore * 0.5);

                multiSeasonBounceBackScore = ((ceilingScore * 0.40) + (discountScore * 0.35) + (skillIntactScore * 0.25)) * (0.55 + 0.45 * reliability);
            }
        }

        double finalScore = Math.Max(multiSeasonBounceBackScore, underutilizedGemScore);
        return Math.Round(Clamp100(finalScore), 1);
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
