using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Services;

public class SleeperScoutingService : ISleeperScoutingService
{
    private static readonly Regex MultiTeamRegex = new(@"^(TOT|\d+TM)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        int latestDatasetSeason = statList.Max(s => s.Season);

        // Preserve raw stints for Two-Way NBA+GLeague display detection, while each engine deduplicates same-(Season,League) multi-team splits.
        var careerByPlayerId = statList
            .GroupBy(s => s.PlayerId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.Season).ThenByDescending(s => s.LeagueDifficultyCoefficient).ToList());

        // Compute per-league max games played per season so sample-size confidence distinguishes finished seasons (~30-82 GP) from early-season sprints (< 22 GP).
        var leagueMaxGpBySeason = statList
            .GroupBy(s => (s.Season, League: (s.League ?? string.Empty).Trim().ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.Max(s => s.GamesPlayed));

        string cleanCat = categoryFilter?.Trim().ToLowerInvariant() ?? string.Empty;

        // seasonFilter == 0 explicitly activates the multi-season career consistency engine
        if (seasonFilter.HasValue && seasonFilter.Value == 0)
        {
            return DiscoverMultiSeasonSleepers(careerByPlayerId, latestDatasetSeason, cleanCat, leagueFilter, top);
        }

        return DiscoverSingleSeasonSleepers(careerByPlayerId, latestDatasetSeason, leagueMaxGpBySeason, cleanCat, leagueFilter, seasonFilter, top);
    }

    private List<SleeperPlayerDto> DiscoverSingleSeasonSleepers(
        Dictionary<Guid, List<PlayerSeasonStat>> careerByPlayerId,
        int latestDatasetSeason,
        Dictionary<(int Season, string League), int> leagueMaxGpBySeason,
        string cleanCat,
        string? leagueFilter,
        int? targetSeason,
        int top)
    {
        var results = new List<SleeperPlayerDto>();

        bool runForgottenPeak = string.IsNullOrEmpty(cleanCat) || cleanCat.Contains("peak") || cleanCat.Contains("bounce") || cleanCat.Contains("forgotten");
        bool runLowMinute = string.IsNullOrEmpty(cleanCat) || cleanCat.Contains("minute") || cleanCat.Contains("per36") || cleanCat.Contains("per40") || cleanCat.Contains("monster");
        bool runStepUp = string.IsNullOrEmpty(cleanCat) || cleanCat.Contains("step") || cleanCat.Contains("euro") || cleanCat.Contains("domestic");

        string? normalizedLeagueFilter = string.IsNullOrWhiteSpace(leagueFilter) ? null : leagueFilter.Trim();

        foreach (var kvp in careerByPlayerId)
        {
            var rawCareer = kvp.Value;
            var dedupedCareer = DeduplicateCareerStints(rawCareer);

            PlayerSeasonStat? latestStat;
            if (targetSeason.HasValue && targetSeason.Value > 0)
            {
                var seasonCandidates = dedupedCareer.Where(s => s.Season == targetSeason.Value);
                if (normalizedLeagueFilter != null)
                {
                    seasonCandidates = seasonCandidates.Where(s => string.Equals(s.League, normalizedLeagueFilter, StringComparison.OrdinalIgnoreCase));
                }
                latestStat = seasonCandidates.OrderByDescending(s => s.GamesPlayed).ThenByDescending(s => s.LeagueDifficultyCoefficient).FirstOrDefault();
            }
            else
            {
                var recentCandidates = normalizedLeagueFilter != null
                    ? dedupedCareer.Where(s => string.Equals(s.League, normalizedLeagueFilter, StringComparison.OrdinalIgnoreCase))
                    : dedupedCareer;

                latestStat = recentCandidates
                    .OrderByDescending(s => s.Season)
                    .ThenByDescending(s => s.GamesPlayed)
                    .ThenByDescending(s => s.LeagueDifficultyCoefficient)
                    .FirstOrDefault();

                // Recency guard for "All Seasons / Latest" (seasonFilter == null): exclude retired/inactive players absent from recent two seasons.
                if (latestStat != null && latestStat.Season < latestDatasetSeason - 1)
                {
                    continue;
                }
            }

            if (latestStat?.Player == null) continue;

            // Enforce minimum sample size across all sleeper categories
            if (latestStat.GamesPlayed < 5 || (latestStat.Minutes ?? 0m) < 10.0m)
            {
                continue;
            }

            var player = latestStat.Player;
            var seasonStints = rawCareer.Where(s => s.Season == latestStat.Season).ToList();

            int? age = CalculateAge(player, latestStat.Season);
            if (age.HasValue && age.Value > 29)
            {
                continue;
            }

            string leagueKey = (latestStat.League ?? string.Empty).Trim().ToUpperInvariant();
            int maxLeagueGp = leagueMaxGpBySeason.TryGetValue((latestStat.Season, leagueKey), out int mgp) ? mgp : latestStat.GamesPlayed;
            bool isMatureSeason = maxLeagueGp >= 22;
            int distinctSeasons = dedupedCareer.Where(s => s.Season <= latestStat.Season).Select(s => s.Season).Distinct().Count();
            int totalPriorTopTierGp = dedupedCareer
                .Where(s => s.Season <= latestStat.Season && s.LeagueDifficultyCoefficient >= 0.86m)
                .Sum(s => s.GamesPlayed);

            // Guard against aging international veterans whose BirthDate is missing in upstream feeds
            if (!age.HasValue && (distinctSeasons >= 6 || totalPriorTopTierGp >= 150))
            {
                continue;
            }

            decimal sanitizedTs = GetSanitizedTs(latestStat.TrueShootingPct);
            decimal scoringTs = Math.Min(0.720m, sanitizedTs);
            bool isEstablishedRegular = IsEstablishedTopTierRegular(latestStat, dedupedCareer);

            // Sample-size Bayesian confidence penalty for small-sample cameos in completed seasons or Showcase splits
            double samplePenalty = isMatureSeason && latestStat.GamesPlayed < 16
                ? (16 - latestStat.GamesPlayed) * 0.85
                : (maxLeagueGp >= 12 && latestStat.GamesPlayed < 12 ? (12 - latestStat.GamesPlayed) * 0.65 : 0.0);
            double unknownAgePenalty = !age.HasValue
                ? (distinctSeasons >= 3 ? 3.0 : 1.0)
                : 0.0;

            // =========================================================================
            // ALGORITHM 1: "The Forgotten Peak" (Bounce-Back Candidates)
            // =========================================================================
            if (runForgottenPeak && (!age.HasValue || age.Value <= 28))
            {
                // Active established starters or heavy rotation regulars are NOT forgotten buy-low candidates
                bool isEstablishedStar = (latestStat.Minutes ?? 0m) >= 22.0m
                    || (latestStat.Points ?? 0m) >= 15.0m
                    || ((latestStat.Minutes ?? 0m) >= 18.5m && latestStat.GamesPlayed >= 35);

                if (!isEstablishedStar && totalPriorTopTierGp < 180 && sanitizedTs >= 0.480m)
                {
                    var priorPeak = dedupedCareer
                        .Where(s => s.Season < latestStat.Season && s.Season >= latestStat.Season - 3)
                        .Where(s => s.GamesPlayed >= 15 && (s.Minutes ?? 0m) >= 18.0m)
                        .Where(s => GetSanitizedTs(s.TrueShootingPct) >= 0.57m || (s.Pir ?? s.PlayerEfficiencyRating ?? 0m) >= 14.0m || (s.GameScoreAverage ?? 0m) >= 10.5m)
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
                            decimal currentPer36Pts = Math.Min(32m, latestStat.PtsPer36 ?? 12m);
                            double rawScore = 62.5
                                + (double)peakImpact * 1.45
                                + (double)currentPer36Pts * 0.45
                                + (double)Math.Max(0m, (scoringTs - 0.54m) * 100m) * 0.35
                                - samplePenalty * 0.5
                                - unknownAgePenalty;

                            if (age.HasValue)
                            {
                                if (age.Value <= 25) rawScore += (26 - age.Value) * 0.9;
                                else if (age.Value >= 28) rawScore -= (age.Value - 27) * 1.0;
                            }

                            double score = CompressScore(rawScore);

                            var reasons = new List<string>
                            {
                                $"Forgotten Peak in {priorPeak.Season} ({priorPeak.League} - {ExpandTeamName(priorPeak.TeamName)}): {priorPeak.Points:F1} PPG, {priorPeak.Minutes:F1} MPG, TS% {GetSanitizedTs(priorPeak.TrueShootingPct) * 100:F1}%, PIR/PER {(priorPeak.Pir ?? priorPeak.PlayerEfficiencyRating ?? priorPeak.GameScoreAverage ?? 0m):F1}",
                                $"Buy-Low Opportunity in {latestStat.Season} ({latestStat.League} - {ExpandTeamName(latestStat.TeamName)}): Role/minutes dipped to {latestStat.Minutes:F1} MPG across {latestStat.GamesPlayed} GP ({latestStat.Points:F1} PPG, {latestStat.PtsPer36:F1} Pts/36)"
                            };

                            results.Add(MapToDto(player, latestStat, age, "ForgottenPeak", score,
                                $"{priorPeak.Season} {priorPeak.League} ({ExpandTeamName(priorPeak.TeamName)}): {priorPeak.Points:F1} PPG / {(priorPeak.Pir ?? priorPeak.PlayerEfficiencyRating ?? 0m):F1} Eff in {priorPeak.Minutes:F1} MPG",
                                reasons,
                                seasonStints));
                        }
                    }
                }
            }

            // =========================================================================
            // ALGORITHM 2: "The Low-Minute Monster" (Per-36 / Per-40 Breakouts)
            // =========================================================================
            if (runLowMinute && !isEstablishedRegular)
            {
                decimal mpg = latestStat.Minutes ?? 0m;
                decimal pts36 = Math.Min(34m, latestStat.PtsPer36 ?? 0m);
                decimal reb36 = Math.Min(18m, latestStat.RebPer36 ?? 0m);
                decimal ast36 = Math.Min(13m, latestStat.AstPer36 ?? 0m);
                decimal astTov = latestStat.AssistToTurnoverRatio ?? 1.5m;
                decimal tov36 = latestStat.TovPer36 ?? 2.5m;
                decimal paceMult = string.Equals(latestStat.League, "GLeague", StringComparison.OrdinalIgnoreCase) ? 0.88m : 1.00m;

                bool inLowMinuteWindow = mpg >= 10.0m && mpg <= 18.5m && latestStat.GamesPlayed >= 5;
                bool hasMonsterPer36 = pts36 * paceMult >= 18.0m || reb36 * paceMult >= 10.0m || ast36 * paceMult >= 7.0m;
                bool hasEliteEfficiency = sanitizedTs >= 0.58m && (astTov >= 1.8m || tov36 <= 2.8m);

                if (inLowMinuteWindow && hasMonsterPer36 && hasEliteEfficiency)
                {
                    double rawScore = 54.5
                        + (double)Math.Max(0m, pts36 * paceMult - 13m) * 1.25
                        + Math.Min(7.0, (double)Math.Max(0m, reb36 * paceMult - 5m) * 0.85)
                        + (double)Math.Max(0m, ast36 * paceMult - 3.5m) * 1.15
                        + (double)Math.Max(0m, (scoringTs - 0.54m) * 100m) * 0.75
                        + (double)latestStat.LeagueDifficultyCoefficient * 10.0
                        - samplePenalty
                        - unknownAgePenalty;

                    if (age.HasValue)
                    {
                        if (age.Value <= 24) rawScore += (25 - age.Value) * 1.3;
                        else if (age.Value >= 28) rawScore -= (age.Value - 27) * 1.0;
                    }

                    double score = CompressScore(rawScore);

                    var reasons = new List<string>
                    {
                        $"Low-Minute Breakout ({mpg:F1} MPG in {latestStat.GamesPlayed} GP): Explodes for {latestStat.PtsPer36:F1} PTS/36 ({latestStat.PointsPer40:F1} PTS/40), {latestStat.RebPer36:F1} REB/36, {latestStat.AstPer36:F1} AST/36",
                        $"Hyper-Efficient Low-Usage Producer: {(sanitizedTs * 100):F1}% TS%, AST/TOV {astTov:F2}, USG% {(latestStat.UsagePct ?? 0m):F1}%"
                    };

                    results.Add(MapToDto(player, latestStat, age, "LowMinuteMonster", score, null, reasons, seasonStints));
                }
            }

            // =========================================================================
            // ALGORITHM 3: "Euro/Domestic & NCAA Ready to Step Up"
            // =========================================================================
            if (runStepUp && !isEstablishedRegular)
            {
                decimal coeff = latestStat.LeagueDifficultyCoefficient;
                bool isGLeague = string.Equals(latestStat.League, "GLeague", StringComparison.OrdinalIgnoreCase);
                bool isNcaa = string.Equals(latestStat.League, "NCAAD1", StringComparison.OrdinalIgnoreCase);
                bool isTier2Or3League = coeff >= 0.68m && coeff <= 0.85m;
                bool isEligibleNcaaProspect = isNcaa && (!age.HasValue || age.Value <= 23);

                // Require meaningful game sample: >= 10 GP in G-League (prevents 5-GP Showcase hot streaks), >= 15 GP in mature NCAAD1
                int minStepUpGp = isGLeague && maxLeagueGp >= 12 ? 10
                    : isNcaa && isMatureSeason ? 15
                    : 5;

                decimal gmSc = latestStat.GameScoreAverage ?? 0m;
                decimal pir = latestStat.Pir ?? gmSc;
                decimal adjustedImpact = Math.Round(coeff * Math.Max(gmSc, pir * 0.85m), 2);
                decimal paceAdjustedImpact = isGLeague ? Math.Round(adjustedImpact * 0.84m, 2) : adjustedImpact;

                decimal fg3Pct = latestStat.Fg3Pct ?? 0m;
                decimal fg3a = latestStat.ThreePointsAttempted ?? 0m;
                decimal total3pa = fg3a * latestStat.GamesPlayed;
                decimal ast36 = latestStat.AstPer36 ?? 0m;
                decimal astTov = latestStat.AssistToTurnoverRatio ?? 1.5m;
                decimal reb36 = latestStat.RebPer36 ?? 0m;
                decimal stocksPer36 = (latestStat.StlPer36 ?? 0m) + (latestStat.BlkPer36 ?? 0m);

                // Transferable archetype gates
                bool hasTransferableShooting = fg3a >= 2.0m && total3pa >= 15.0m && fg3Pct >= 0.36m;
                bool hasTransferableDefense = stocksPer36 >= 2.2m;
                bool hasEliteEfficiency = sanitizedTs >= 0.59m && paceAdjustedImpact >= 8.5m;
                bool hasTransferablePlaymaking = isTier2Or3League && ast36 >= 6.0m && astTov >= 1.85m && sanitizedTs >= 0.54m && paceAdjustedImpact >= 8.2m;
                bool hasYoungInteriorProduction = isTier2Or3League && age.HasValue && age.Value <= 24 && reb36 >= 9.0m && sanitizedTs >= 0.57m && paceAdjustedImpact >= 8.0m;

                decimal minImpactRequired = isEligibleNcaaProspect ? 9.2m : 7.5m;
                bool passesLeagueAndSample = (isTier2Or3League || isEligibleNcaaProspect)
                    && latestStat.GamesPlayed >= minStepUpGp
                    && (latestStat.Minutes ?? 0m) >= (isEligibleNcaaProspect ? 20.0m : 10.0m)
                    && paceAdjustedImpact >= minImpactRequired;

                bool passesSkillGate = isEligibleNcaaProspect
                    ? (hasTransferableShooting || hasTransferableDefense || (sanitizedTs >= 0.59m && (stocksPer36 >= 2.0m || ast36 >= 5.5m)))
                    : (hasTransferableShooting || hasTransferableDefense || hasEliteEfficiency || hasTransferablePlaymaking || hasYoungInteriorProduction);

                if (passesLeagueAndSample && passesSkillGate)
                {
                    // Year-over-year progression bonus for ascending domestic/European players
                    var prevStat = dedupedCareer
                        .Where(s => s.Season == latestStat.Season - 1 && s.GamesPlayed >= 10)
                        .OrderByDescending(s => s.GamesPlayed)
                        .FirstOrDefault();
                    double yoyProgressionBonus = 0.0;
                    if (prevStat != null && (!age.HasValue || age.Value <= 25))
                    {
                        decimal prevImpact = prevStat.LeagueDifficultyCoefficient * (prevStat.GameScoreAverage ?? prevStat.Pir ?? 0m);
                        if (paceAdjustedImpact - prevImpact >= 2.0m)
                        {
                            yoyProgressionBonus = Math.Min(3.0, (double)(paceAdjustedImpact - prevImpact) * 0.65);
                        }
                    }

                    double rawScore = 51.0
                        + (double)paceAdjustedImpact * 1.95
                        + (double)(Math.Min(0.70m, scoringTs) * 14.0m)
                        + (hasTransferableShooting ? (double)Math.Min(2.8m, fg3a * 0.55m) : 0.0)
                        + (hasTransferableDefense ? 1.5 : 0.0)
                        + (hasTransferablePlaymaking ? 1.8 : 0.0)
                        + yoyProgressionBonus
                        - samplePenalty
                        - unknownAgePenalty;

                    if (age.HasValue)
                    {
                        if (age.Value <= 23) rawScore += (24 - age.Value) * 1.65;
                        else if (age.Value <= 25) rawScore += 1.5;
                        else if (age.Value >= 28) rawScore -= (age.Value - 27) * 1.1;
                    }

                    double score = CompressScore(rawScore);

                    var reasons = new List<string>
                    {
                        $"Tier-Adjusted Standout in {latestStat.League} (Coeff {coeff:F2}): Pace-Adjusted Impact {paceAdjustedImpact:F1} (GmSc {gmSc:F1}, PIR {latestStat.Pir:F1})"
                    };

                    if (hasTransferableShooting)
                        reasons.Add($"Transferable Floor Spacing: {(fg3Pct * 100):F1}% 3P on {fg3a:F1} 3PA/G (eFG% {((latestStat.EffectiveFgPct ?? 0m) * 100):F1}%)");
                    if (hasTransferableDefense)
                        reasons.Add($"Switchable Defensive Disruption: {stocksPer36:F1} Stocks/36 ({latestStat.StlPer36:F1} STL/36, {latestStat.BlkPer36:F1} BLK/36)");
                    if (hasTransferablePlaymaking)
                        reasons.Add($"High-Level Playmaking Engine: {ast36:F1} AST/36 with {astTov:F2} AST/TOV ratio");
                    if (hasEliteEfficiency && !hasTransferableShooting && !hasTransferableDefense)
                        reasons.Add($"Elite Interior/Finishing Efficiency: TS% {(sanitizedTs * 100):F1}%, Effective FG% {((latestStat.EffectiveFgPct ?? 0m) * 100):F1}%");
                    if (yoyProgressionBonus > 0.5)
                        reasons.Add($"Strong Year-over-Year Ascent vs {latestStat.Season - 1} ({prevStat!.League})");

                    results.Add(MapToDto(player, latestStat, age, "StepUpCandidate", score, null, reasons, seasonStints));
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

    private List<SleeperPlayerDto> DiscoverMultiSeasonSleepers(
        Dictionary<Guid, List<PlayerSeasonStat>> careerByPlayerId,
        int latestDatasetSeason,
        string cleanCat,
        string? leagueFilter,
        int top)
    {
        var results = new List<SleeperPlayerDto>();
        int minModernSeason = Math.Max(2020, latestDatasetSeason - 6);
        int recencyFloor = Math.Max(2024, latestDatasetSeason - 1);
        string? normalizedLeagueFilter = string.IsNullOrWhiteSpace(leagueFilter) ? null : leagueFilter.Trim();

        foreach (var kvp in careerByPlayerId)
        {
            var rawCareer = kvp.Value;
            var dedupedCareer = DeduplicateCareerStints(rawCareer);

            var modernStats = dedupedCareer
                .Where(s => s.Season >= minModernSeason && s.Season <= latestDatasetSeason && s.GamesPlayed > 0)
                .ToList();
            if (!modernStats.Any()) continue;

            if (normalizedLeagueFilter != null)
            {
                modernStats = modernStats
                    .Where(s => string.Equals(s.League, normalizedLeagueFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!modernStats.Any()) continue;
            }

            var player = modernStats.First().Player;
            if (player == null) continue;

            int seasonsCount = modernStats.Select(s => s.Season).Distinct().Count();
            if (seasonsCount < 2) continue;

            int minSeason = modernStats.Min(s => s.Season);
            int maxSeason = modernStats.Max(s => s.Season);
            // Dynamic recency: active in latest 2 dataset seasons (e.g. 2026–2027)
            if (maxSeason < recencyFloor) continue;

            int totalGP = modernStats.Sum(s => s.GamesPlayed);
            // Minimum career sample size: at least 35 games across the modern era (prevents micro-sample flukes)
            if (totalGP < 35) continue;

            decimal totalMins = CalculateTotalMinutes(modernStats);
            if (totalMins <= 0) continue;

            decimal careerMpg = Math.Round(totalMins / totalGP, 1);
            if (careerMpg < 10.5m) continue;

            decimal careerPoints = Math.Round(modernStats.Sum(s => (s.Points ?? 0m) * s.GamesPlayed) / totalGP, 1);
            decimal careerRebounds = Math.Round(modernStats.Sum(s => (s.Rebounds ?? 0m) * s.GamesPlayed) / totalGP, 1);
            decimal careerAssists = Math.Round(modernStats.Sum(s => (s.Assists ?? 0m) * s.GamesPlayed) / totalGP, 1);
            decimal careerTov = Math.Round(modernStats.Sum(s => (s.Turnovers ?? 0m) * s.GamesPlayed) / totalGP, 1);
            decimal careerTs = Math.Round(modernStats.Sum(s => (s.TrueShootingPct.HasValue ? GetSanitizedTs(s.TrueShootingPct) : 0.52m) * s.GamesPlayed) / totalGP, 3);
            decimal careerPts36 = Math.Round(modernStats.Sum(s => Math.Min(34m, s.PtsPer36 ?? 12m) * s.GamesPlayed) / totalGP, 1);
            decimal careerReb36 = Math.Round(modernStats.Sum(s => Math.Min(18m, s.RebPer36 ?? 5m) * s.GamesPlayed) / totalGP, 1);
            decimal careerAst36 = Math.Round(modernStats.Sum(s => Math.Min(13m, s.AstPer36 ?? 3m) * s.GamesPlayed) / totalGP, 1);
            decimal careerStl36 = Math.Round(modernStats.Sum(s => (s.StlPer36 ?? 1m) * s.GamesPlayed) / totalGP, 1);
            decimal careerBlk36 = Math.Round(modernStats.Sum(s => (s.BlkPer36 ?? 0.5m) * s.GamesPlayed) / totalGP, 1);
            decimal careerPir = Math.Round(modernStats.Sum(s => (s.Pir ?? s.PlayerEfficiencyRating ?? s.GameScoreAverage ?? 10m) * s.GamesPlayed) / totalGP, 1);
            decimal careerCoeff = Math.Round(modernStats.Sum(s => s.LeagueDifficultyCoefficient * s.GamesPlayed) / totalGP, 2);

            // Use latest deduplicated stint for current team, league display, and top-tier regular check
            var latestStint = modernStats.OrderByDescending(s => s.Season).ThenByDescending(s => s.GamesPlayed).First();

            // Exclude established superstars/starters (career MPG >= 22 && PPG >= 15 OR already proven NBA/EL rotation fixture)
            if (careerMpg >= 22.0m && careerPoints >= 15.0m)
            {
                continue;
            }

            if (IsEstablishedTopTierRegular(latestStint, dedupedCareer))
            {
                continue;
            }

            // G-League pace normalization on career per-36 rates when predominantly G-League
            decimal gLeagueShare = (decimal)modernStats
                .Where(s => string.Equals(s.League, "GLeague", StringComparison.OrdinalIgnoreCase))
                .Sum(s => s.GamesPlayed) / totalGP;
            decimal careerPaceMult = 1.00m - (0.12m * gLeagueShare);

            // Sleeper Traits across era:
            bool isHighPer36 = careerPts36 * careerPaceMult >= 16.0m || careerReb36 * careerPaceMult >= 8.5m || careerAst36 * careerPaceMult >= 5.5m;
            bool isEfficient = careerTs >= 0.550m || careerPir >= 11.0m;
            bool isRolePlayer = careerMpg <= 21.5m;

            if (!isHighPer36 || !isEfficient || !isRolePlayer)
            {
                continue;
            }

            // Category filter check if specified
            if (!string.IsNullOrEmpty(cleanCat))
            {
                if (cleanCat.Contains("minute") && careerMpg > 18.0m) continue;
                if (cleanCat.Contains("peak") && seasonsCount < 3) continue;
                if (cleanCat.Contains("step") && careerCoeff > 0.85m) continue;
            }

            // Strict age filter: Age evaluated as of current era. Players 30+ are veterans, not sleeper prospects.
            int? age = CalculateAge(player, Math.Max(latestDatasetSeason, maxSeason));
            if (age.HasValue && age.Value > 29)
            {
                continue;
            }
            if (!age.HasValue && seasonsCount >= 5)
            {
                continue;
            }

            decimal career3pm = Math.Round(modernStats.Sum(s => (s.ThreePointsMade ?? ((s.ThreePointsAttempted ?? 0m) * (s.Fg3Pct ?? 0m))) * s.GamesPlayed) / totalGP, 1);
            decimal career3pa = Math.Round(modernStats.Sum(s => (s.ThreePointsAttempted ?? 0m) * s.GamesPlayed) / totalGP, 1);
            decimal career3pPct = career3pa > 0 ? Math.Round(career3pm / career3pa, 3) : 0m;

            decimal nbaGpShare = (decimal)modernStats
                .Where(s => string.Equals(s.League, "NBA", StringComparison.OrdinalIgnoreCase))
                .Sum(s => s.GamesPlayed) / totalGP;

            // Curb interior rim-runner TS% inflation (>62.5%) vs scalable perimeter shot-makers and reward 3P spacing
            decimal effectiveTsCap = (career3pm >= 1.0m || career3pa >= 3.0m) ? 0.660m : 0.625m;
            decimal boundedTs = Math.Min(effectiveTsCap, careerTs);
            double tsContribution = (double)Math.Max(0m, (boundedTs - 0.52m) * 100m) * 0.58;

            double shootingBonus = (career3pa >= 1.5m && career3pPct >= 0.340m)
                ? Math.Min(3.6, (double)(career3pPct - 0.330m) * 28.0 + Math.Min(1.5, (double)career3pm * 0.65))
                : 0.0;

            double rawScore = 51.0
                + (double)Math.Max(0m, careerPts36 * careerPaceMult - 12m) * 1.25
                + Math.Min(4.8, (double)Math.Max(0m, careerReb36 * careerPaceMult - 5.5m) * 0.50)
                + (double)Math.Max(0m, careerAst36 * careerPaceMult - 2.5m) * 1.35
                + Math.Min(1.8, (double)Math.Max(0m, careerStl36 * careerPaceMult - 1.1m) * 1.10)
                + tsContribution
                + shootingBonus
                + (double)careerCoeff * 9.0
                + Math.Min(3.2, (seasonsCount - 1) * 0.9)
                + Math.Min(2.2, (totalGP / 85.0));

            // Reduce dominance of 26+ NBA garbage-time/bench veterans vs rising U-24 & top European breakouts
            if (nbaGpShare >= 0.60m && careerMpg <= 16.5m && (!age.HasValue || age.Value >= 26))
            {
                rawScore -= 3.8;
            }

            if (age.HasValue)
            {
                if (age.Value <= 23) rawScore += 2.5 + (23 - age.Value) * 1.6;
                else if (age.Value == 24) rawScore += 1.8;
                else if (age.Value == 25) rawScore += 0.4;
                else rawScore -= (age.Value - 25) * 1.3;
            }
            else
            {
                rawScore -= (seasonsCount >= 4 ? 3.5 : 1.5);
            }

            double score = CompressScore(rawScore);

            var (resolvedTeam, parentTeam, isTwoWay) = ResolveTeamDisplay(latestStint, player, rawCareer.Where(s => s.Season == latestStint.Season).ToList());

            var reasons = new List<string>
            {
                $"Multi-Season Consistency ({minSeason}–{maxSeason}, {seasonsCount} Seasons): {careerPts36:F1} PTS/36 ({careerPoints:F1} PPG, {careerReb36:F1} REB/36) across {totalGP} games in {careerMpg:F1} MPG",
                $"Career-Long Elite Efficiency: {(careerTs * 100):F1}% TS, {careerPir:F1} Avg PIR / Efficiency (League Difficulty {careerCoeff:F2})"
            };

            if (isTwoWay && !string.IsNullOrWhiteSpace(parentTeam))
            {
                reasons.Add($"Two-Way Contract Experience: Under {parentTeam} NBA rights while providing rotation depth in {latestStint.League}.");
            }

            string resolvedPos = !string.IsNullOrWhiteSpace(player.PrimaryPosition) ? player.PrimaryPosition
                               : !string.IsNullOrWhiteSpace(player.Position) ? player.Position
                               : "F";

            string resolvedNat = (!string.IsNullOrWhiteSpace(player.Nationality) && player.Nationality != "INTL")
                               ? player.Nationality
                               : (Player.DetectNationality(player.FirstName, player.LastName, resolvedTeam)
                                  ?? (latestStint.League == "NBA" || latestStint.League == "GLeague" ? "USA" : "—"));

            results.Add(new SleeperPlayerDto
            {
                PlayerId = player.Id,
                FullName = !string.IsNullOrWhiteSpace(player.FullName) ? player.FullName : $"{player.FirstName} {player.LastName}".Trim(),
                Position = resolvedPos,
                Nationality = resolvedNat,
                Age = age,
                Season = maxSeason,
                League = latestStint.League,
                TeamName = resolvedTeam,
                ParentTeam = parentTeam,
                IsTwoWayPlayer = isTwoWay,
                LeagueDifficultyCoefficient = careerCoeff,
                SleeperCategory = "MultiSeasonConsistency",
                SleeperScore = Math.Round(score, 1),
                DifficultyAdjustedGameScore = Math.Round(careerPir * careerCoeff, 2),
                GamesPlayed = totalGP,
                MinutesPerGame = careerMpg,
                Points = careerPoints,
                Rebounds = careerRebounds,
                Assists = careerAssists,
                PtsPer36 = careerPts36,
                RebPer36 = careerReb36,
                AstPer36 = careerAst36,
                StlPer36 = careerStl36,
                BlkPer36 = careerBlk36,
                TovPer36 = careerTov,
                PointsPer40 = Math.Round(careerPts36 * 40m / 36m, 1),
                TrueShootingPct = careerTs,
                EffectiveFgPct = Math.Round(careerTs * 0.95m, 3),
                Fg3Pct = career3pPct,
                ThreePointsAttempted = career3pa,
                ThreePointsMade = career3pm,
                UsagePct = Math.Round(modernStats.Average(s => s.UsagePct ?? 18m), 1),
                AssistToTurnoverRatio = careerTov > 0 ? Math.Round(careerAssists / careerTov, 2) : 1.5m,
                GameScoreAverage = careerPir,
                Pir = careerPir,
                StocksPer36 = Math.Round(careerStl36 + careerBlk36, 1),
                ThreePointRate = career3pa > 0 ? Math.Round(career3pa / (careerPoints / 1.5m), 3) : null,
                EuroLeagueTransitionIndex = modernStats.Max(s => s.EuroLeagueTransitionIndex),
                EstimatedMarketValueTier = player.EstimatedMarketValueTier,
                ContractExpirySeason = player.ContractExpirySeason,
                IsWatchlisted = player.IsWatchlisted,
                PeakSeasonContext = $"{minSeason}–{maxSeason} ({seasonsCount}-Season Stability)",
                ScoutingReasons = reasons
            });
        }

        return results
            .GroupBy(r => !string.IsNullOrWhiteSpace(r.FullName) ? Player.NormalizeName(r.FullName, "") : r.PlayerId.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.SleeperScore).First())
            .OrderByDescending(r => r.SleeperScore)
            .Take(top)
            .ToList();
    }

    private static List<PlayerSeasonStat> DeduplicateCareerStints(List<PlayerSeasonStat> career)
    {
        return career
            .GroupBy(s => (s.Season, League: (s.League ?? string.Empty).Trim().ToUpperInvariant()))
            .Select(g =>
            {
                var aggregate = g
                    .Where(s => !string.IsNullOrWhiteSpace(s.TeamName) && MultiTeamRegex.IsMatch(s.TeamName.Trim()))
                    .OrderByDescending(s => s.GamesPlayed)
                    .ThenByDescending(s => s.Minutes ?? 0m)
                    .FirstOrDefault();
                if (aggregate != null) return aggregate;

                return g
                    .OrderByDescending(s => s.GamesPlayed)
                    .ThenByDescending(s => s.Minutes ?? 0m)
                    .First();
            })
            .OrderBy(s => s.Season)
            .ThenByDescending(s => s.LeagueDifficultyCoefficient)
            .ToList();
    }

    private static decimal GetSanitizedTs(decimal? ts) =>
        ts.HasValue ? Math.Min(0.850m, Math.Max(0m, ts.Value)) : 0m;

    private static bool IsEstablishedTopTierRegular(PlayerSeasonStat stat, IReadOnlyList<PlayerSeasonStat> dedupedCareer)
    {
        // NBA (1.00) & EuroLeague (0.90): exclude players already playing heavy rotation/starter minutes or scoring volume
        if (stat.LeagueDifficultyCoefficient >= 0.86m)
        {
            decimal mpg = stat.Minutes ?? 0m;
            decimal pts = stat.Points ?? 0m;
            decimal pir = stat.Pir ?? stat.PlayerEfficiencyRating ?? stat.GameScoreAverage ?? 0m;

            if (mpg >= 20.0m && stat.GamesPlayed >= 15) return true;
            if (pts >= 12.0m && stat.GamesPlayed >= 12) return true;
            if (mpg >= 19.0m && (pts >= 10.0m || pir >= 11.5m) && stat.GamesPlayed >= 20) return true;
        }

        // Check prior/current NBA or EuroLeague seasons for proven established starter or long-tenured regular status
        var priorTopTier = dedupedCareer
            .Where(s => s.Season <= stat.Season && s.LeagueDifficultyCoefficient >= 0.86m)
            .ToList();

        if (priorTopTier.Any(s =>
                ((s.Minutes ?? 0m) >= 22.0m && s.GamesPlayed >= 20 && ((s.Points ?? 0m) >= 10.5m || (s.GameScoreAverage ?? 0m) >= 9.0m))
                || ((s.Minutes ?? 0m) >= 21.0m && s.GamesPlayed >= 30)))
        {
            return true;
        }

        int totalTopTierGp = priorTopTier.Sum(s => s.GamesPlayed);
        decimal weightedTopTierMpg = totalTopTierGp > 0
            ? CalculateTotalMinutes(priorTopTier) / totalTopTierGp
            : 0m;

        // Proven multi-year NBA/EL fixture (~14.5+ MPG over 135+ top-tier games) is already discovered
        return totalTopTierGp >= 135 && weightedTopTierMpg >= 14.5m;
    }

    private static decimal CalculateTotalMinutes(IEnumerable<PlayerSeasonStat> stats)
    {
        decimal total = 0m;
        foreach (var s in stats)
        {
            if (s.MinutesPlayedTotal.HasValue && s.MinutesPlayedTotal.Value > 0)
            {
                total += s.MinutesPlayedTotal.Value;
            }
            else if (s.Minutes.HasValue && s.GamesPlayed > 0)
            {
                total += s.Minutes.Value * s.GamesPlayed;
            }
        }
        return total;
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

    private static readonly Dictionary<string, string> TeamNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // NBA Teams
        { "BOS", "Boston Celtics" },
        { "BKN", "Brooklyn Nets" },
        { "NYK", "New York Knicks" },
        { "PHI", "Philadelphia 76ers" },
        { "TOR", "Toronto Raptors" },
        { "CHI", "Chicago Bulls" },
        { "CLE", "Cleveland Cavaliers" },
        { "DET", "Detroit Pistons" },
        { "IND", "Indiana Pacers" },
        { "MIL", "Milwaukee Bucks" },
        { "ATL", "Atlanta Hawks" },
        { "CHA", "Charlotte Hornets" },
        { "MIA", "Miami Heat" },
        { "ORL", "Orlando Magic" },
        { "WAS", "Washington Wizards" },
        { "DEN", "Denver Nuggets" },
        { "MIN", "Minnesota Timberwolves" },
        { "OKC", "Oklahoma City Thunder" },
        { "POR", "Portland Trail Blazers" },
        { "UTA", "Utah Jazz" },
        { "GSW", "Golden State Warriors" },
        { "LAC", "LA Clippers" },
        { "LAL", "LA Lakers" },
        { "PHX", "Phoenix Suns" },
        { "SAC", "Sacramento Kings" },
        { "DAL", "Dallas Mavericks" },
        { "HOU", "Houston Rockets" },
        { "MEM", "Memphis Grizzlies" },
        { "NOP", "New Orleans Pelicans" },
        { "SAS", "San Antonio Spurs" },

        // G-League Affiliates
        { "MNE", "Maine Celtics" },
        { "SLC", "Salt Lake City Stars" },
        { "WCB", "Windy City Bulls" },
        { "OSC", "Osceola Magic" },
        { "RGV", "Rio Grande Valley Vipers" },
        { "MHU", "Memphis Hustle" },
        { "FWN", "Fort Wayne Mad Ants" },
        { "LIN", "Long Island Nets" },
        { "WES", "Westchester Knicks" },
        { "DEL", "Delaware Blue Coats" },
        { "RAP", "Raptors 905" },
        { "CCG", "Cleveland Charge" },
        { "MCC", "Motor City Cruise" },
        { "WIS", "Wisconsin Herd" },
        { "CPB", "College Park Skyhawks" },
        { "GBO", "Greensboro Swarm" },
        { "SXF", "Sioux Falls Skyforce" },
        { "CAP", "Capital City Go-Go" },
        { "GRG", "Grand Rapids Gold" },
        { "IOW", "Iowa Wolves" },
        { "OKL", "Oklahoma City Blue" },
        { "RIP", "Rip City Remix" },
        { "SCW", "Santa Cruz Warriors" },
        { "ONT", "Ontario Clippers" },
        { "SBK", "South Bay Lakers" },
        { "VAL", "Valley Suns" },
        { "STO", "Stockton Kings" },
        { "TEX", "Texas Legends" },
        { "AUS", "Austin Spurs" },
        { "BIR", "Birmingham Squadron" }
    };

    public static string ExpandTeamName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Free Agent / Unassigned";
        return TeamNameMap.TryGetValue(code.Trim(), out var fullName) ? fullName : code;
    }

    private static (string resolvedTeam, string? parentTeam, bool isTwoWay) ResolveTeamDisplay(
        PlayerSeasonStat stat,
        Player player,
        List<PlayerSeasonStat>? seasonStints)
    {
        string currentTeamExpanded = ExpandTeamName(stat.TeamName);
        if (seasonStints == null || seasonStints.Count <= 1)
        {
            string? fallbackParent = !string.IsNullOrWhiteSpace(player.TeamName) ? ExpandTeamName(player.TeamName) : null;
            return (currentTeamExpanded, fallbackParent, false);
        }

        var nbaStint = seasonStints.FirstOrDefault(s => s.League == "NBA");
        var gLeagueStint = seasonStints.FirstOrDefault(s => s.League == "GLeague");

        if (nbaStint != null && gLeagueStint != null)
        {
            string parentNbaTeam = !string.IsNullOrWhiteSpace(nbaStint.TeamName) && nbaStint.TeamName != "TOT"
                ? ExpandTeamName(nbaStint.TeamName)
                : (!string.IsNullOrWhiteSpace(player.TeamName) ? ExpandTeamName(player.TeamName) : "NBA");
            string gLeagueTeam = ExpandTeamName(gLeagueStint.TeamName);

            if (stat.League == "GLeague")
            {
                return ($"{gLeagueTeam} ({parentNbaTeam} 2-Way)", parentNbaTeam, true);
            }
            else
            {
                return ($"{parentNbaTeam} (G-League: {gLeagueTeam})", parentNbaTeam, true);
            }
        }

        string? pTeam = !string.IsNullOrWhiteSpace(player.TeamName) ? ExpandTeamName(player.TeamName) : null;
        return (currentTeamExpanded, pTeam, false);
    }

    private static SleeperPlayerDto MapToDto(
        Player player,
        PlayerSeasonStat stat,
        int? age,
        string category,
        double score,
        string? peakContext,
        List<string> reasons,
        List<PlayerSeasonStat>? seasonStints = null)
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

        var (resolvedTeam, parentTeam, isTwoWay) = ResolveTeamDisplay(stat, player, seasonStints);

        if (isTwoWay && !string.IsNullOrWhiteSpace(parentTeam) && !reasons.Any(r => r.Contains("Two-Way")))
        {
            reasons.Insert(0, $"Two-Way Contract: Rotation role in {stat.League} under {parentTeam} NBA rights in {stat.Season} ({stat.Points:F1} PPG, {stat.Minutes:F1} MPG)");
        }

        return new SleeperPlayerDto
        {
            PlayerId = player.Id,
            FullName = !string.IsNullOrWhiteSpace(player.FullName) ? player.FullName : $"{player.FirstName} {player.LastName}".Trim(),
            Position = resolvedPos,
            Nationality = resolvedNat,
            Age = age,
            Season = stat.Season,
            League = stat.League,
            TeamName = resolvedTeam,
            ParentTeam = parentTeam,
            IsTwoWayPlayer = isTwoWay,
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
            TrueShootingPct = stat.TrueShootingPct.HasValue ? GetSanitizedTs(stat.TrueShootingPct) : null,
            EffectiveFgPct = stat.EffectiveFgPct.HasValue ? Math.Min(1.000m, stat.EffectiveFgPct.Value) : null,
            Fg3Pct = stat.Fg3Pct,
            ThreePointsAttempted = stat.ThreePointsAttempted,
            ThreePointsMade = stat.ThreePointsMade ?? (stat.ThreePointsAttempted.HasValue && stat.Fg3Pct.HasValue ? Math.Round(stat.ThreePointsAttempted.Value * stat.Fg3Pct.Value, 1) : null),
            UsagePct = stat.UsagePct,
            AssistToTurnoverRatio = stat.AssistToTurnoverRatio,
            GameScoreAverage = stat.GameScoreAverage,
            Pir = stat.Pir,
            StocksPer36 = stat.StocksPer36 ?? ((stat.StlPer36 ?? 0m) + (stat.BlkPer36 ?? 0m)),
            ThreePointRate = stat.ThreePointRate,
            EuroLeagueTransitionIndex = stat.EuroLeagueTransitionIndex,
            EstimatedMarketValueTier = player.EstimatedMarketValueTier,
            ContractExpirySeason = player.ContractExpirySeason,
            IsWatchlisted = player.IsWatchlisted,
            PeakSeasonContext = peakContext,
            ScoutingReasons = reasons
        };
    }

    private static readonly Dictionary<string, int> KnownPlayerBirthYears = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Nando De Colo", 1987 },
        { "Milos Teodosic", 1987 },
        { "Jimmer Fredette", 1989 },
        { "Miroslav Raduljica", 1988 },
        { "Marco Belinelli", 1987 },
        { "Thaddus McFadden", 1987 },
        { "Sergio Llull", 1987 },
        { "Sergio Rodriguez", 1986 },
        { "Rudy Fernandez", 1985 },
        { "Kostas Sloukas", 1990 },
        { "Nick Calathes", 1990 },
        { "Aaron Doornekamp", 1985 },
        { "Maxime Courby", 1990 },
        { "Joffrey Lauvergne", 1991 },
        { "Nemanja Nedovic", 1991 },
        { "Brandon Davies", 1991 },
        { "Gavin Ware", 1993 },
        { "Filip Bundovic", 1994 },
        { "Nikola Milutinov", 1994 },
        { "Youssoupha Fall", 1995 },
        { "Steve Vasturia", 1995 },
        { "Alberto Abalde", 1995 },
        { "Wesley Van Beck", 1996 },
        { "Mathis Dossou-Yovo", 1997 },
        { "Khalifa Koumadje", 1997 },
        { "Nick Ward", 1997 },
        { "Danko Brankovic", 2000 },
        { "Mario Nakic", 2001 }
    };

    public static int? CalculateAge(Player? player, int seasonEndYear)
    {
        int refYear = seasonEndYear > 0 ? seasonEndYear : DateTime.UtcNow.Year;

        if (player?.BirthDate.HasValue == true)
        {
            var dob = player.BirthDate.Value;
            int age = refYear - dob.Year;
            return (age >= 14 && age <= 52) ? age : null;
        }

        if (player != null && !string.IsNullOrWhiteSpace(player.FullName) &&
            KnownPlayerBirthYears.TryGetValue(player.FullName.Trim(), out int birthYear))
        {
            int age = refYear - birthYear;
            return (age >= 14 && age <= 52) ? age : null;
        }

        return null;
    }
}
