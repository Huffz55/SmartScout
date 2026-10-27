using System;
using System.Collections.Generic;
using System.Linq;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Services;

public class SleeperScoutingService : ISleeperScoutingService
{
    public List<SleeperPlayerDto> DiscoverSleepers(
        IEnumerable<PlayerSeasonStat> allSeasonStats,
        string? categoryFilter = null,
        string? leagueFilter = null,
        int? seasonFilter = null,
        int top = 25)
    {
        var statList = allSeasonStats.ToList();
        if (!statList.Any())
            return new List<SleeperPlayerDto>();

        var careerByPlayerId = statList
            .GroupBy(s => s.PlayerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.Season).ThenByDescending(s => s.LeagueDifficultyCoefficient).ToList());

        var results = new List<SleeperPlayerDto>();
        string cleanCat = categoryFilter?.Trim().ToLowerInvariant() ?? string.Empty;

        bool runForgottenPeak = string.IsNullOrEmpty(cleanCat) || cleanCat.Contains("peak") || cleanCat.Contains("bounce") || cleanCat.Contains("forgotten");
        bool runLowMinute = string.IsNullOrEmpty(cleanCat) || cleanCat.Contains("minute") || cleanCat.Contains("per36") || cleanCat.Contains("per40") || cleanCat.Contains("monster");
        bool runStepUp = string.IsNullOrEmpty(cleanCat) || cleanCat.Contains("step") || cleanCat.Contains("euro") || cleanCat.Contains("domestic");

        foreach (var kvp in careerByPlayerId)
        {
            var career = kvp.Value;
            var latestStat = seasonFilter.HasValue
                ? career.Where(s => s.Season == seasonFilter.Value).OrderByDescending(s => s.GamesPlayed).FirstOrDefault()
                : career.OrderByDescending(s => s.Season).ThenByDescending(s => s.GamesPlayed).FirstOrDefault();

            if (latestStat?.Player == null) continue;

            // Enforce minimum sample size across all sleeper categories
            if (latestStat.GamesPlayed < 5 || (latestStat.Minutes ?? 0m) < 10.0m)
            {
                continue;
            }

            var player = latestStat.Player;

            if (!string.IsNullOrWhiteSpace(leagueFilter) &&
                !string.Equals(latestStat.League, leagueFilter.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int? age = CalculateAge(player.BirthDate, latestStat.Season);

            // =========================================================================
            // ALGORITHM 1: "The Forgotten Peak" (Bounce-Back Candidates)
            // =========================================================================
            if (runForgottenPeak && (!age.HasValue || age.Value <= 29))
            {
                var priorPeak = career
                    .Where(s => s.Season < latestStat.Season && s.Season >= latestStat.Season - 3)
                    .Where(s => s.GamesPlayed >= 15 && (s.Minutes ?? 0m) >= 18.0m)
                    .Where(s => (s.TrueShootingPct ?? 0m) >= 0.57m || (s.Pir ?? s.PlayerEfficiencyRating ?? 0m) >= 14.0m || (s.GameScoreAverage ?? 0m) >= 10.5m)
                    .OrderByDescending(s => (s.GameScoreAverage ?? 0m) * s.LeagueDifficultyCoefficient)
                    .FirstOrDefault();

                if (priorPeak != null)
                {
                    bool minutesDropped = (latestStat.Minutes ?? 0m) < 15.0m || ((priorPeak.Minutes ?? 0m) - (latestStat.Minutes ?? 0m) >= 5.0m);
                    bool gamesDropped = latestStat.GamesPlayed < 20;
                    bool scoringDip = ((priorPeak.Points ?? 0m) - (latestStat.Points ?? 0m)) >= 3.5m;

                    if (minutesDropped || gamesDropped || scoringDip)
                    {
                        decimal peakImpact = (priorPeak.GameScoreAverage ?? priorPeak.Points ?? 10m) * priorPeak.LeagueDifficultyCoefficient;
                        decimal currentPer36Pts = latestStat.PtsPer36 ?? 12m;
                        double rawScore = 56.0 + (double)peakImpact * 1.5 + (double)currentPer36Pts * 0.5;

                        // Age adjustment only if age is known
                        if (age.HasValue)
                        {
                            if (age.Value <= 25) rawScore += 2.0;
                            else if (age.Value >= 30) rawScore -= (age.Value - 29) * 0.8;
                        }

                        double score = CompressScore(rawScore);

                        var reasons = new List<string>
                        {
                            $"Forgotten Peak in {priorPeak.Season} ({priorPeak.League} - {priorPeak.TeamName}): {priorPeak.Points:F1} PPG, {priorPeak.Minutes:F1} MPG, TS% {(priorPeak.TrueShootingPct ?? 0m) * 100:F1}%, PIR/PER {(priorPeak.Pir ?? priorPeak.PlayerEfficiencyRating ?? priorPeak.GameScoreAverage ?? 0m):F1}",
                            $"Buy-Low Opportunity in {latestStat.Season} ({latestStat.League} - {latestStat.TeamName}): Role/minutes dipped to {latestStat.Minutes:F1} MPG across {latestStat.GamesPlayed} GP ({latestStat.Points:F1} PPG, {latestStat.PtsPer36:F1} Pts/36)"
                        };

                        results.Add(MapToDto(player, latestStat, age, "ForgottenPeak", score,
                            $"{priorPeak.Season} {priorPeak.League} ({priorPeak.TeamName}): {priorPeak.Points:F1} PPG / {(priorPeak.Pir ?? priorPeak.PlayerEfficiencyRating ?? 0m):F1} Eff in {priorPeak.Minutes:F1} MPG",
                            reasons));
                    }
                }
            }

            // =========================================================================
            // ALGORITHM 2: "The Low-Minute Monster" (Per-36 / Per-40 Breakouts)
            // =========================================================================
            if (runLowMinute)
            {
                decimal mpg = latestStat.Minutes ?? 0m;
                decimal ts = latestStat.TrueShootingPct ?? 0m;
                decimal pts36 = latestStat.PtsPer36 ?? 0m;
                decimal reb36 = latestStat.RebPer36 ?? 0m;
                decimal ast36 = latestStat.AstPer36 ?? 0m;
                decimal astTov = latestStat.AssistToTurnoverRatio ?? 1.5m;
                decimal tov36 = latestStat.TovPer36 ?? 2.5m;

                bool inLowMinuteWindow = mpg >= 10.0m && mpg <= 18.5m && latestStat.GamesPlayed >= 5;
                bool hasMonsterPer36 = pts36 >= 18.0m || reb36 >= 10.0m || ast36 >= 7.0m;
                bool hasEliteEfficiency = ts >= 0.58m && (astTov >= 1.8m || tov36 <= 2.8m);

                if (inLowMinuteWindow && hasMonsterPer36 && hasEliteEfficiency)
                {
                    double rawScore = 55.0
                        + (double)Math.Max(0m, pts36 - 13m) * 1.3
                        + (double)Math.Max(0m, reb36 - 5m) * 1.0
                        + (double)Math.Max(0m, ast36 - 3.5m) * 1.2
                        + (double)Math.Max(0m, (ts - 0.54m) * 100m) * 0.8
                        + (double)latestStat.LeagueDifficultyCoefficient * 9.0;

                    if (age.HasValue)
                    {
                        if (age.Value <= 24) rawScore += (25 - age.Value) * 1.2;
                        else if (age.Value >= 31) rawScore -= (age.Value - 30) * 0.75;
                    }

                    double score = CompressScore(rawScore);

                    var reasons = new List<string>
                    {
                        $"Low-Minute Breakout ({mpg:F1} MPG in {latestStat.GamesPlayed} GP): Explodes for {pts36:F1} PTS/36 ({latestStat.PointsPer40:F1} PTS/40), {reb36:F1} REB/36, {ast36:F1} AST/36",
                        $"Hyper-Efficient Low-Usage Producer: {(ts * 100):F1}% TS%, AST/TOV {astTov:F2}, USG% {(latestStat.UsagePct ?? 0m):F1}%"
                    };

                    results.Add(MapToDto(player, latestStat, age, "LowMinuteMonster", score, null, reasons));
                }
            }

            // =========================================================================
            // ALGORITHM 3: "Euro/Domestic Ready to Step Up"
            // =========================================================================
            if (runStepUp)
            {
                decimal coeff = latestStat.LeagueDifficultyCoefficient;
                bool isTier2Or3League = coeff >= 0.68m && coeff <= 0.85m;
                decimal gmSc = latestStat.GameScoreAverage ?? 0m;
                decimal pir = latestStat.Pir ?? gmSc;
                decimal adjustedImpact = Math.Round(coeff * Math.Max(gmSc, pir * 0.85m), 2);

                decimal fg3Pct = latestStat.Fg3Pct ?? 0m;
                decimal fg3a = latestStat.ThreePointsAttempted ?? 0m;
                decimal total3pa = fg3a * latestStat.GamesPlayed;
                decimal stocksPer36 = (latestStat.StlPer36 ?? 0m) + (latestStat.BlkPer36 ?? 0m);
                decimal ts = latestStat.TrueShootingPct ?? 0m;

                // STRICT: ThreePointsAttempted >= 2.0 AND total 3PA >= 15.0 AND 3P% >= 36%
                bool hasTransferableShooting = fg3a >= 2.0m && total3pa >= 15.0m && fg3Pct >= 0.36m;
                bool hasTransferableDefense = stocksPer36 >= 2.2m;
                bool hasEliteEfficiency = ts >= 0.59m && adjustedImpact >= 8.5m;

                if (isTier2Or3League && latestStat.GamesPlayed >= 5 && (latestStat.Minutes ?? 0m) >= 10.0m && adjustedImpact >= 7.5m &&
                    (hasTransferableShooting || hasTransferableDefense || hasEliteEfficiency))
                {
                    double rawScore = 54.0
                        + (double)adjustedImpact * 2.2
                        + (double)(ts * 18m)
                        + (hasTransferableShooting ? (double)Math.Min(3.0m, fg3a * 0.6m) : 0.0)
                        + (hasTransferableDefense ? 1.5 : 0.0);

                    if (age.HasValue)
                    {
                        if (age.Value <= 23) rawScore += (24 - age.Value) * 1.5;
                        else if (age.Value <= 25) rawScore += 1.0;
                        else if (age.Value >= 31) rawScore -= (age.Value - 30) * 0.75;
                    }

                    double score = CompressScore(rawScore);

                    var reasons = new List<string>
                    {
                        $"Tier-Adjusted Standout in {latestStat.League} (Coeff {coeff:F2}): Difficulty-Adjusted Impact {adjustedImpact:F1} (GmSc {gmSc:F1}, PIR {latestStat.Pir:F1})"
                    };

                    if (hasTransferableShooting)
                        reasons.Add($"Transferable Floor Spacing: {(fg3Pct * 100):F1}% 3P on {fg3a:F1} 3PA/G (eFG% {((latestStat.EffectiveFgPct ?? 0m) * 100):F1}%)");
                    if (hasTransferableDefense)
                        reasons.Add($"Switchable Defensive Disruption: {stocksPer36:F1} Stocks/36 ({latestStat.StlPer36:F1} STL/36, {latestStat.BlkPer36:F1} BLK/36)");
                    if (hasEliteEfficiency && !hasTransferableShooting && !hasTransferableDefense)
                        reasons.Add($"Elite Interior/Finishing Efficiency: TS% {(ts * 100):F1}%, Effective FG% {((latestStat.EffectiveFgPct ?? 0m) * 100):F1}%");

                    results.Add(MapToDto(player, latestStat, age, "StepUpCandidate", score, null, reasons));
                }
            }
        }

        // Deduplicate output by player normalized name: guarantees no player appears twice across categories
        return results
            .GroupBy(r => !string.IsNullOrWhiteSpace(r.FullName) ? Player.NormalizeName(r.FullName, "") : r.PlayerId.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.SleeperScore).ThenByDescending(r => r.DifficultyAdjustedGameScore).First())
            .OrderByDescending(r => r.SleeperScore)
            .ThenByDescending(r => r.DifficultyAdjustedGameScore)
            .Take(top)
            .ToList();
    }

    private static double CompressScore(double rawScore)
    {
        if (rawScore <= 80.0)
        {
            return Math.Max(65.0, Math.Round(rawScore, 1));
        }

        // Asymptotic non-linear saturation curve to spread top scores naturally between 80.0 and 98.8
        double scaled = 80.0 + 18.8 * (1.0 - Math.Exp(-(rawScore - 80.0) / 14.0));
        return Math.Round(Math.Min(98.8, scaled), 1);
    }

    private static SleeperPlayerDto MapToDto(
        Player player,
        PlayerSeasonStat stat,
        int? age,
        string category,
        double score,
        string? peakContext,
        List<string> reasons)
    {
        decimal gmSc = stat.GameScoreAverage ?? stat.Pir ?? stat.Points ?? 0m;
        string resolvedPos = !string.IsNullOrWhiteSpace(player.PrimaryPosition) ? player.PrimaryPosition
                           : !string.IsNullOrWhiteSpace(player.Position) ? player.Position
                           : !string.IsNullOrWhiteSpace(stat.Player?.Position) ? stat.Player.Position
                           : "F";

        string resolvedNat = (!string.IsNullOrWhiteSpace(player.Nationality) && player.Nationality != "INTL")
                           ? player.Nationality
                           : (Player.DetectNationality(player.FirstName, player.LastName, stat.TeamName)
                              ?? (stat.League == "NBA" || stat.League == "GLeague" ? "USA" : "—"));

        return new SleeperPlayerDto
        {
            PlayerId = player.Id,
            FullName = !string.IsNullOrWhiteSpace(player.FullName) ? player.FullName : $"{player.FirstName} {player.LastName}".Trim(),
            Position = resolvedPos,
            Nationality = resolvedNat,
            Age = age,
            Season = stat.Season,
            League = stat.League,
            TeamName = stat.TeamName,
            LeagueDifficultyCoefficient = stat.LeagueDifficultyCoefficient,
            SleeperCategory = category,
            SleeperScore = Math.Round(score, 1),
            DifficultyAdjustedGameScore = Math.Round(gmSc * stat.LeagueDifficultyCoefficient, 2),
            GamesPlayed = stat.GamesPlayed,
            MinutesPerGame = stat.Minutes,
            Points = stat.Points,
            Rebounds = stat.Rebounds,
            Assists = stat.Assists,
            PtsPer36 = stat.PtsPer36,
            RebPer36 = stat.RebPer36,
            AstPer36 = stat.AstPer36,
            StlPer36 = stat.StlPer36,
            BlkPer36 = stat.BlkPer36,
            TovPer36 = stat.TovPer36,
            PointsPer40 = stat.PointsPer40,
            TrueShootingPct = stat.TrueShootingPct,
            EffectiveFgPct = stat.EffectiveFgPct,
            Fg3Pct = stat.Fg3Pct,
            ThreePointsAttempted = stat.ThreePointsAttempted,
            UsagePct = stat.UsagePct,
            AssistToTurnoverRatio = stat.AssistToTurnoverRatio,
            GameScoreAverage = stat.GameScoreAverage,
            Pir = stat.Pir,
            PeakSeasonContext = peakContext,
            ScoutingReasons = reasons
        };
    }

    private static int? CalculateAge(DateTime? birthDate, int seasonEndYear)
    {
        if (!birthDate.HasValue) return null;
        int age = seasonEndYear - birthDate.Value.Year;
        return (age >= 15 && age <= 50) ? age : null;
    }
}
