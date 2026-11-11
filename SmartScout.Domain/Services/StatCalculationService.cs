using System;
using SmartScout.Domain.Entities;

namespace SmartScout.Domain.Services;

/// <summary>
/// Domain service that mathematically computes Per-36, Per-40, True Shooting % (TS%),
/// Effective Field Goal % (eFG%), Assist-to-Turnover Ratio (AST/TOV), Hollinger's Game Score,
/// European PIR (Valuation), and estimated Usage Rate (USG%) so no analytics column is ever missing.
/// </summary>
public static class StatCalculationService
{
    public static void EnrichAndCalculate(PlayerSeasonStat stat)
    {
        decimal coeff = BasketballLeague.GetDifficultyCoefficient(stat.League);
        decimal? minutes = stat.Minutes;

        // 1. Per-36 Normalized Metrics
        decimal? ptsPer36 = CalculatePer36(stat.Points, minutes);
        decimal? rebPer36 = CalculatePer36(stat.Rebounds, minutes);
        decimal? astPer36 = CalculatePer36(stat.Assists, minutes);
        decimal? stlPer36 = CalculatePer36(stat.Steals, minutes);
        decimal? blkPer36 = CalculatePer36(stat.Blocks, minutes);
        decimal? tovPer36 = CalculatePer36(stat.Turnovers, minutes);

        // 2. Per-40 Normalized Metrics
        decimal? ptsPer40 = stat.PointsPer40 ?? CalculatePer40(stat.Points, minutes);
        decimal? rebPer40 = stat.ReboundsPer40 ?? CalculatePer40(stat.Rebounds, minutes);
        decimal? astPer40 = stat.AssistsPer40 ?? CalculatePer40(stat.Assists, minutes);

        // 3. True Shooting % (TS%) = PTS / (2 * (FGA + 0.44 * FTA))
        decimal? tsPct = stat.TrueShootingPct ?? CalculateTrueShootingPct(
            stat.Points,
            stat.FieldGoalsAttempted,
            stat.FreeThrowsAttempted,
            stat.FgPct,
            stat.Fg3Pct,
            stat.FtPct,
            stat.FieldGoalsMade,
            stat.ThreePointsMade,
            stat.FreeThrowsMade);

        // 4. Effective Field Goal % (eFG%) = (FGM + 0.5 * 3PM) / FGA
        decimal? eFgPct = stat.EffectiveFgPct ?? CalculateEffectiveFgPct(
            stat.FieldGoalsMade,
            stat.ThreePointsMade,
            stat.FieldGoalsAttempted,
            stat.FgPct,
            stat.Fg3Pct);

        // 5. Assist-to-Turnover Ratio (AST / TOV)
        decimal? astTov = stat.AssistToTurnoverRatio ?? CalculateAssistToTurnoverRatio(stat.Assists, stat.Turnovers);

        // 6. Hollinger's Game Score = PTS + 0.4*FGM - 0.7*FGA - 0.4*(FTA - FTM) + 0.7*ORB + 0.3*DRB + STL + 0.7*AST + 0.7*BLK - 0.4*PF - TOV
        decimal? gameScore = stat.GameScoreAverage ?? CalculateHollingerGameScore(
            stat.Points,
            stat.FieldGoalsMade,
            stat.FieldGoalsAttempted,
            stat.FreeThrowsMade,
            stat.FreeThrowsAttempted,
            stat.OffensiveRebounds,
            stat.DefensiveRebounds,
            stat.Rebounds,
            stat.Steals,
            stat.Assists,
            stat.Blocks,
            stat.PersonalFoulsCommitted,
            stat.Turnovers,
            stat.FgPct,
            stat.FtPct);

        // 7. European Performance Index Rating (PIR) = (PTS + REB + AST + STL + BLK + PFD) - ((FGA - FGM) + (FTA - FTM) + TOV + PF)
        decimal? pir = stat.Pir ?? CalculateEuropeanPir(
            stat.Points,
            stat.Rebounds,
            stat.Assists,
            stat.Steals,
            stat.Blocks,
            stat.FoulsDrawn,
            stat.FieldGoalsMade,
            stat.FieldGoalsAttempted,
            stat.FreeThrowsMade,
            stat.FreeThrowsAttempted,
            stat.Turnovers,
            stat.PersonalFoulsCommitted,
            stat.FgPct,
            stat.FtPct);

        // 8. Usage Rate (USG%) estimation when team totals aren't provided
        decimal? usgPct = stat.UsagePct ?? EstimateUsageRate(
            stat.Points,
            stat.FieldGoalsAttempted,
            stat.FreeThrowsAttempted,
            stat.Turnovers,
            minutes,
            stat.FgPct);

        // 9. Modern Defensive Stocks Per 36 (Steals + Blocks)
        decimal? stocksPer36 = (stlPer36.HasValue || blkPer36.HasValue)
            ? (stlPer36 ?? 0m) + (blkPer36 ?? 0m)
            : null;

        // 10. Modern 3-Point Rate (3PA / FGA Spacing Metric)
        decimal? threePointRate = (stat.ThreePointsAttempted.HasValue && stat.FieldGoalsAttempted.HasValue && stat.FieldGoalsAttempted.Value > 0)
            ? Math.Round(stat.ThreePointsAttempted.Value / stat.FieldGoalsAttempted.Value, 3)
            : null;

        // 11. EuroLeague Transition Index (0 - 100 Success Expectation)
        decimal? transitionIndex = stat.EuroLeagueTransitionIndex ?? CalculateEuroLeagueTransitionIndex(
            stat,
            stat.Player?.CurrentAge,
            stat.Player?.HeightCm);

        stat.SetCalculatedMetrics(
            leagueDifficultyCoefficient: coeff,
            ptsPer36: ptsPer36,
            rebPer36: rebPer36,
            astPer36: astPer36,
            stlPer36: stlPer36,
            blkPer36: blkPer36,
            tovPer36: tovPer36,
            ptsPer40: ptsPer40,
            rebPer40: rebPer40,
            astPer40: astPer40,
            tsPct: tsPct,
            eFgPct: eFgPct,
            usgPct: usgPct,
            astTovRatio: astTov,
            gameScoreAvg: gameScore,
            pirVal: pir,
            stocksPer36: stocksPer36,
            threePointRate: threePointRate,
            transitionIndex: transitionIndex);
    }

    public static decimal? CalculatePer36(decimal? statPerGame, decimal? minutesPerGame)
    {
        if (!statPerGame.HasValue || !minutesPerGame.HasValue || minutesPerGame.Value <= 0)
            return null;
        return Math.Round((statPerGame.Value / minutesPerGame.Value) * 36m, 1);
    }

    public static decimal? CalculatePer40(decimal? statPerGame, decimal? minutesPerGame)
    {
        if (!statPerGame.HasValue || !minutesPerGame.HasValue || minutesPerGame.Value <= 0)
            return null;
        return Math.Round((statPerGame.Value / minutesPerGame.Value) * 40m, 1);
    }

    public static decimal? CalculateTrueShootingPct(
        decimal? points,
        decimal? fga,
        decimal? fta,
        decimal? fgPct = null,
        decimal? fg3Pct = null,
        decimal? ftPct = null,
        decimal? fgm = null,
        decimal? fg3m = null,
        decimal? ftm = null)
    {
        if (!points.HasValue || points.Value <= 0) return null;

        if (fga.HasValue && fta.HasValue)
        {
            decimal tsa = fga.Value + 0.44m * fta.Value;
            if (tsa > 0)
            {
                decimal rawTs = points.Value / (2m * tsa);
                bool hasShotMismatch = fgm.HasValue && fg3m.HasValue && ftm.HasValue
                    && Math.Abs(points.Value - ((2m * fgm.Value) + fg3m.Value + ftm.Value)) > 0.8m
                    && rawTs > 0.680m;

                // Guard against upstream boxscore shot corruption (e.g., partial FGA making PTS/(2*TSA) > 0.90 or LNB missing 2PA/2PM)
                if (!hasShotMismatch && rawTs <= 0.880m && points.Value <= (fga.Value * 3m + fta.Value + 0.15m))
                {
                    return Math.Round(rawTs, 3);
                }

                if (fgPct.HasValue && fgPct.Value > 0 && fgPct.Value <= 1.0m)
                {
                    decimal fallbackTs = fgPct.Value + ((fg3Pct ?? 0.33m) * 0.18m) + (((ftPct ?? 0.75m) - 0.70m) * 0.15m);
                    decimal maxCap = hasShotMismatch ? 0.740m : 0.820m;
                    return Math.Round(Math.Clamp(fallbackTs, 0.300m, maxCap), 3);
                }

                return Math.Round(Math.Clamp(rawTs, 0m, 0.850m), 3);
            }
        }

        // Fallback estimation when only percentages & points are available
        if (fgPct.HasValue && fgPct.Value > 0)
        {
            decimal estTs = fgPct.Value + ((fg3Pct ?? 0.33m) * 0.18m) + (((ftPct ?? 0.75m) - 0.70m) * 0.15m);
            return Math.Round(Math.Clamp(estTs, 0.300m, 0.850m), 3);
        }

        return null;
    }

    public static decimal? CalculateEffectiveFgPct(
        decimal? fgm,
        decimal? fg3m,
        decimal? fga,
        decimal? fgPct = null,
        decimal? fg3Pct = null)
    {
        if (fgm.HasValue && fga.HasValue && fga.Value > 0)
        {
            decimal threeMade = fg3m ?? 0m;
            decimal rawEfg = (fgm.Value + 0.5m * threeMade) / fga.Value;
            if (rawEfg <= 1.000m && threeMade <= fgm.Value + 0.05m)
            {
                return Math.Round(rawEfg, 3);
            }

            if (fgPct.HasValue && fgPct.Value > 0 && fgPct.Value <= 1.0m)
            {
                decimal threeBonus = (fg3Pct ?? 0m) * 0.175m;
                return Math.Round(Math.Clamp(fgPct.Value + threeBonus, 0m, 1.000m), 3);
            }

            return Math.Round(Math.Clamp(rawEfg, 0m, 1.000m), 3);
        }

        if (fgPct.HasValue && fgPct.Value > 0)
        {
            decimal threeBonus = (fg3Pct ?? 0m) * 0.175m; // ~35% 3PA share * 0.5
            return Math.Round(Math.Clamp(fgPct.Value + threeBonus, 0m, 1.000m), 3);
        }

        return null;
    }

    public static decimal? CalculateAssistToTurnoverRatio(decimal? assists, decimal? turnovers)
    {
        if (!assists.HasValue) return null;
        if (!turnovers.HasValue || turnovers.Value <= 0)
        {
            return assists.Value > 0 ? Math.Round(assists.Value, 2) : 0m;
        }
        return Math.Round(assists.Value / turnovers.Value, 2);
    }

    public static decimal? CalculateHollingerGameScore(
        decimal? points,
        decimal? fgm,
        decimal? fga,
        decimal? ftm,
        decimal? fta,
        decimal? orb,
        decimal? drb,
        decimal? totalReb,
        decimal? steals,
        decimal? assists,
        decimal? blocks,
        decimal? pf,
        decimal? turnovers,
        decimal? fgPct = null,
        decimal? ftPct = null)
    {
        if (!points.HasValue) return null;

        decimal pts = points.Value;
        decimal estFga = fga ?? (fgPct.HasValue && fgPct.Value > 0 ? (pts * 0.42m) / fgPct.Value : pts * 0.85m);
        decimal estFgm = fgm ?? (fgPct.HasValue ? estFga * fgPct.Value : pts * 0.38m);
        decimal estFta = fta ?? (pts * 0.24m);
        decimal estFtm = ftm ?? (estFta * (ftPct ?? 0.76m));

        decimal trb = totalReb ?? ((orb ?? 0m) + (drb ?? 0m));
        decimal estOrb = orb ?? (trb * 0.25m);
        decimal estDrb = drb ?? (trb * 0.75m);
        decimal stl = steals ?? 0m;
        decimal ast = assists ?? 0m;
        decimal blk = blocks ?? 0m;
        decimal fouls = pf ?? 2.0m;
        decimal tov = turnovers ?? 1.5m;

        decimal gmSc = pts
                       + (0.4m * estFgm)
                       - (0.7m * estFga)
                       - (0.4m * Math.Max(0m, estFta - estFtm))
                       + (0.7m * estOrb)
                       + (0.3m * estDrb)
                       + stl
                       + (0.7m * ast)
                       + (0.7m * blk)
                       - (0.4m * fouls)
                       - tov;

        return Math.Round(gmSc, 1);
    }

    public static decimal? CalculateEuropeanPir(
        decimal? points,
        decimal? totalReb,
        decimal? assists,
        decimal? steals,
        decimal? blocks,
        decimal? foulsDrawn,
        decimal? fgm,
        decimal? fga,
        decimal? ftm,
        decimal? fta,
        decimal? turnovers,
        decimal? pfCommitted,
        decimal? fgPct = null,
        decimal? ftPct = null)
    {
        if (!points.HasValue) return null;

        decimal pts = points.Value;
        decimal reb = totalReb ?? 0m;
        decimal ast = assists ?? 0m;
        decimal stl = steals ?? 0m;
        decimal blk = blocks ?? 0m;
        decimal pfd = foulsDrawn ?? (pts * 0.20m);

        decimal estFga = fga ?? (fgPct.HasValue && fgPct.Value > 0 ? (pts * 0.42m) / fgPct.Value : pts * 0.85m);
        decimal estFgm = fgm ?? (fgPct.HasValue ? estFga * fgPct.Value : pts * 0.38m);
        decimal missedFg = Math.Max(0m, estFga - estFgm);

        decimal estFta = fta ?? (pts * 0.24m);
        decimal estFtm = ftm ?? (estFta * (ftPct ?? 0.76m));
        decimal missedFt = Math.Max(0m, estFta - estFtm);

        decimal tov = turnovers ?? 1.5m;
        decimal pf = pfCommitted ?? 2.0m;

        decimal pir = (pts + reb + ast + stl + blk + pfd) - (missedFg + missedFt + tov + pf);
        return Math.Round(pir, 1);
    }

    public static decimal? EstimateUsageRate(
        decimal? points,
        decimal? fga,
        decimal? fta,
        decimal? turnovers,
        decimal? minutesPerGame,
        decimal? fgPct = null)
    {
        if (!minutesPerGame.HasValue || minutesPerGame.Value <= 0 || !points.HasValue)
            return null;

        decimal pts = points.Value;
        decimal estFga = fga ?? (fgPct.HasValue && fgPct.Value > 0 ? (pts * 0.42m) / fgPct.Value : pts * 0.85m);
        decimal estFta = fta ?? (pts * 0.24m);
        decimal tov = turnovers ?? 1.5m;

        decimal playerPossessions = estFga + (0.44m * estFta) + tov;
        // Standard team pace ~75 possessions per 40 minutes (~1.875 team possessions per minute on floor)
        decimal teamPossessionsOnFloor = minutesPerGame.Value * 1.875m;
        if (teamPossessionsOnFloor <= 0) return null;

        decimal usg = (playerPossessions / teamPossessionsOnFloor) * 100m;
        return Math.Round(Math.Clamp(usg, 5.0m, 48.0m), 1);
    }

    public static decimal? CalculateEuroLeagueTransitionIndex(
        PlayerSeasonStat stat,
        int? age = null,
        int? heightCm = null)
    {
        if (string.Equals(stat.League, "EuroLeague", StringComparison.OrdinalIgnoreCase))
            return 94.0m;
        if (string.Equals(stat.League, "NBA", StringComparison.OrdinalIgnoreCase))
            return 98.0m;

        if (!stat.Minutes.HasValue || stat.Minutes.Value < 7.0m || stat.GamesPlayed < 3)
            return null;

        decimal score = 52.0m; // Base for pro tier-2 player

        // 1. True Shooting efficiency impact (+/- 18 pts)
        decimal ts = stat.TrueShootingPct ?? 0.53m;
        score += Math.Clamp((ts - 0.54m) * 65m, -18m, 18m);

        // 2. Volume Scoring per 36 min (+/- 15 pts)
        decimal pts36 = stat.PtsPer36 ?? (stat.Points.HasValue && stat.Minutes > 0 ? (stat.Points.Value / stat.Minutes.Value) * 36m : 10m);
        if (pts36 >= 20m) score += 14m;
        else if (pts36 >= 16m) score += 9m;
        else if (pts36 >= 12m) score += 4m;
        else if (pts36 < 8m) score -= 8m;

        // 3. Playmaking & Decision Making (AST/TOV)
        if (stat.AssistToTurnoverRatio.HasValue)
        {
            if (stat.AssistToTurnoverRatio.Value >= 2.2m) score += 9m;
            else if (stat.AssistToTurnoverRatio.Value >= 1.6m) score += 5m;
            else if (stat.AssistToTurnoverRatio.Value < 0.9m) score -= 6m;
        }

        // 4. Defensive Stocks Activity (Steals + Blocks / 36)
        decimal stocks = (stat.StlPer36 ?? 0m) + (stat.BlkPer36 ?? 0m);
        if (stocks >= 2.4m) score += 8m;
        else if (stocks >= 1.7m) score += 4m;

        // 5. Modern 3-Point Gravity & Spacing
        if (stat.Fg3Pct.HasValue && stat.ThreePointsMade.HasValue)
        {
            if (stat.Fg3Pct.Value >= 0.38m && stat.ThreePointsMade.Value >= 1.5m) score += 7m;
            else if (stat.Fg3Pct.Value >= 0.35m && stat.ThreePointsMade.Value >= 1.0m) score += 4m;
        }

        // 6. Age Upside & Development Trajectory
        if (age.HasValue)
        {
            if (age.Value <= 22) score += 10m;
            else if (age.Value <= 25) score += 6m;
            else if (age.Value <= 28) score += 2m;
            else if (age.Value >= 33) score -= 8m;
        }

        // 7. Free Throw Generation Pressure (Fouls Drawn / FGA)
        if (stat.FoulsDrawn.HasValue && stat.FieldGoalsAttempted.HasValue && stat.FieldGoalsAttempted.Value > 0)
        {
            decimal ftr = stat.FoulsDrawn.Value / stat.FieldGoalsAttempted.Value;
            if (ftr >= 0.38m) score += 6m;
            else if (ftr >= 0.25m) score += 3m;
        }

        // 8. League Difficulty Weight
        decimal diff = BasketballLeague.GetDifficultyCoefficient(stat.League);
        score *= (0.80m + (diff * 0.20m));

        return Math.Clamp(Math.Round(score, 1), 15.0m, 96.0m);
    }
}
