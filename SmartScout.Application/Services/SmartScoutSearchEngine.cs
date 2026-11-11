using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using SmartScout.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmartScout.Application.Services;

public class SmartScoutSearchEngine : ISmartScoutSearchEngine
{
    private readonly IPlayerAnalyticsService _analyticsService;

    public SmartScoutSearchEngine(IPlayerAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    public List<SmartScoutSearchResultDto> Search(
        string prompt, 
        IEnumerable<PlayerSeasonStat> candidateStats, 
        int top = 20,
        int? targetSeason = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return new List<SmartScoutSearchResultDto>();

        var candidateList = candidateStats.ToList();
        if (!candidateList.Any())
            return new List<SmartScoutSearchResultDto>();

        int maxDatasetSeason = candidateList.Max(s => s.Season);
        string cleanPrompt = prompt.ToLowerInvariant();

        // Group career history by PlayerId so multi-season trajectory & buy-low bounce-back can be evaluated
        var careerByPlayerId = candidateList
            .GroupBy(s => s.PlayerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var navStats = g.First().Player?.SeasonStats;
                    return (IReadOnlyList<PlayerSeasonStat>)((navStats != null && navStats.Count > g.Count())
                        ? navStats.OrderBy(x => x.Season).ToList()
                        : g.OrderBy(x => x.Season).ToList());
                });

        // 1. Extract Target Archetypes
        var targetArchetype = DetectArchetype(cleanPrompt);

        // 2. Extract Position Filters
        string? targetPosition = DetectPosition(cleanPrompt);

        // 3. Extract Statistical Intent
        bool wantsScoring = cleanPrompt.Contains("scor") || cleanPrompt.Contains("point") || cleanPrompt.Contains("bucket") || cleanPrompt.Contains("pts");
        bool wantsShooting = cleanPrompt.Contains("shoot") || cleanPrompt.Contains("3pt") || cleanPrompt.Contains("three") || cleanPrompt.Contains("sniper") || cleanPrompt.Contains("spac");
        bool wantsEfficiency = cleanPrompt.Contains("efficien") || cleanPrompt.Contains("true shooting") || cleanPrompt.Contains("ts%");
        bool wantsPlaymaking = cleanPrompt.Contains("playmak") || cleanPrompt.Contains("assist") || cleanPrompt.Contains("vision") || cleanPrompt.Contains("pass");
        bool wantsRebounding = cleanPrompt.Contains("rebound") || cleanPrompt.Contains("board") || cleanPrompt.Contains("glass");
        bool wantsDefense = cleanPrompt.Contains("defen") || cleanPrompt.Contains("lockdown") || cleanPrompt.Contains("steal") || cleanPrompt.Contains("block") || cleanPrompt.Contains("rim protect");
        bool wantsHighImpact = cleanPrompt.Contains("impact") || cleanPrompt.Contains("mvp") || cleanPrompt.Contains("star") || cleanPrompt.Contains("elite") || cleanPrompt.Contains("all-star");

        // European, Beşiktaş & Moneyball Sleeper/Bounce-Back Intent
        bool wantsBesiktasStyle = cleanPrompt.Contains("besiktas") || cleanPrompt.Contains("beşiktaş") || cleanPrompt.Contains("bjk") ||
                                  cleanPrompt.Contains("alimpijevic") || cleanPrompt.Contains("alimpijević") || cleanPrompt.Contains("tam saha pres") ||
                                  cleanPrompt.Contains("ball pressure") || cleanPrompt.Contains("press") || cleanPrompt.Contains("disrupt");
        bool wantsEuropeanFit = cleanPrompt.Contains("euro") || cleanPrompt.Contains("avrupa") || cleanPrompt.Contains("eci") ||
                               cleanPrompt.Contains("half court") || cleanPrompt.Contains("spacing") || cleanPrompt.Contains("fiba");
        bool wantsBuyLowSleeper = cleanPrompt.Contains("uyuyan") || cleanPrompt.Contains("buy low") || cleanPrompt.Contains("buy-low") ||
                                  cleanPrompt.Contains("bounce back") || cleanPrompt.Contains("bounce-back") || cleanPrompt.Contains("parlaya") ||
                                  cleanPrompt.Contains("patlama") || cleanPrompt.Contains("sleeper") || cleanPrompt.Contains("undervalued") ||
                                  cleanPrompt.Contains("firsat") || cleanPrompt.Contains("fırsat") || cleanPrompt.Contains("potansiyel") ||
                                  cleanPrompt.Contains("eskiden") || cleanPrompt.Contains("gecmiste") || cleanPrompt.Contains("geçmişte");
        bool wantsPir = cleanPrompt.Contains("pir") || cleanPrompt.Contains("valuation") || cleanPrompt.Contains("verimlilik");
        bool wantsFoulsDrawn = cleanPrompt.Contains("foul") || cleanPrompt.Contains("faul") || cleanPrompt.Contains("contact");
        bool wantsTurkish = cleanPrompt.Contains("turk") || cleanPrompt.Contains("türk") || cleanPrompt.Contains("yerli");

        bool wantsEuroLeagueOnly = cleanPrompt.Contains("euroleague") && !cleanPrompt.Contains("eurocup");
        bool wantsEuroCupOnly = cleanPrompt.Contains("eurocup");
        bool wantsNbaOnly = cleanPrompt.Contains("nba");

        // 4. Extract explicit numeric thresholds (e.g., "20+ pts", "25 pts")
        decimal minPoints = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:pts|points|sayı|sayi)");
        decimal minAssists = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:ast|assists|asist)");
        decimal minRebounds = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:reb|rebounds|ribaund)");
        decimal minPir = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:pir|valuation|verimlilik)");
        decimal minFoulsDrawn = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:fouls?|faul)");

        // 5. Multi-Constraint NLP Extractions (Age, Height, Contract, Percentage Cutoffs, Negative/Discipline Intents)
        int? maxAge = ExtractAgeCap(cleanPrompt);
        int? minHeightCm = ExtractMinHeightCm(cleanPrompt);
        int? targetContractYear = ExtractContractExpiryYear(cleanPrompt);
        decimal minFg3Pct = ExtractPercentageCutoff(cleanPrompt, @"(?:%\s*(\d{2})|(\d{2})\s*%)\+?\s*(?:3pt|3p|three|üçlük|ucluk)");
        decimal minTsPct = ExtractPercentageCutoff(cleanPrompt, @"(?:%\s*(\d{2})|(\d{2})\s*%)\+?\s*(?:ts|true shooting)");

        bool wantsLowTurnover = cleanPrompt.Contains("low turnover") || cleanPrompt.Contains("low tov") ||
                                cleanPrompt.Contains("düşük top kaybı") || cleanPrompt.Contains("dusuk top kaybi") ||
                                cleanPrompt.Contains("top kaybı yapmayan") || cleanPrompt.Contains("top kaybi yapmayan") ||
                                cleanPrompt.Contains("ball security");
        bool wantsLowFoul = cleanPrompt.Contains("low foul") || cleanPrompt.Contains("foul discipline") ||
                            cleanPrompt.Contains("faul problemi olmayan") || cleanPrompt.Contains("az faul");
        bool wantsLowUsage = cleanPrompt.Contains("low usage") || cleanPrompt.Contains("off-ball") ||
                             cleanPrompt.Contains("off ball") || cleanPrompt.Contains("topsuk") || cleanPrompt.Contains("topsuz");
        bool wantsSwitchable = cleanPrompt.Contains("switch") || cleanPrompt.Contains("wingspan") ||
                               cleanPrompt.Contains("uzun kol") || cleanPrompt.Contains("atletik");

        var parsedConstraints = BuildParsedConstraintsList(
            targetPosition, targetArchetype, maxAge, minHeightCm, targetContractYear,
            minPoints, minAssists, minRebounds, minFg3Pct, minTsPct,
            wantsTurkish, wantsLowTurnover, wantsLowFoul, wantsLowUsage, wantsSwitchable,
            wantsBesiktasStyle, wantsEuropeanFit, wantsBuyLowSleeper);

        var matchedResults = new List<SmartScoutSearchResultDto>();

        foreach (var stat in candidateList)
        {
            var player = stat.Player;
            if (player == null) continue;

            // Strict Filter 1: Games played minimum sample
            if (stat.GamesPlayed < 3) continue;

            careerByPlayerId.TryGetValue(stat.PlayerId, out var careerHistory);

            // If caller specified targetSeason (while pre-loading prior career seasons for buy-low comparison), only score targetSeason rows
            if (targetSeason.HasValue && stat.Season != targetSeason.Value) continue;

            // When searching for Buy-Low / Bounce-Back sleepers across multiple seasons, evaluate the player's latest season against their prior peak
            if (wantsBuyLowSleeper && !targetSeason.HasValue && careerHistory != null && careerHistory.Count > 1)
            {
                int latestSeason = careerHistory.Max(s => s.Season);
                if (stat.Season != latestSeason) continue;
            }

            string statLeague = !string.IsNullOrWhiteSpace(stat.League) ? stat.League : player.League;
            string? statTeam = !string.IsNullOrWhiteSpace(stat.TeamName) ? stat.TeamName : player.TeamName;

            // Strict Filter 2: Explicit position conflict
            if (targetPosition != null && !string.IsNullOrEmpty(player.Position))
            {
                if (targetPosition == "C" && !player.Position.Contains('C') && !player.Position.Contains('F')) continue;
                if (targetPosition == "G" && !player.Position.Contains('G')) continue;
                if (targetPosition == "F" && !player.Position.Contains('F') && !player.Position.Contains('G')) continue;
            }

            // Strict Filter 3: Competition / League restrictions
            if (wantsEuroLeagueOnly && !string.Equals(statLeague, "EuroLeague", StringComparison.OrdinalIgnoreCase)) continue;
            if (wantsEuroCupOnly && !string.Equals(statLeague, "EuroCup", StringComparison.OrdinalIgnoreCase)) continue;
            if (wantsNbaOnly && !string.Equals(statLeague, "NBA", StringComparison.OrdinalIgnoreCase)) continue;

            // Strict Filter 4: Domestic Turkish Passport
            if (wantsTurkish)
            {
                bool isTurkish = string.Equals(player.Nationality, "TUR", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(player.Nationality, "Turkey", StringComparison.OrdinalIgnoreCase);
                if (!isTurkish) continue;
            }

            // Strict Filter 5: Explicit numeric & physical/contract thresholds
            if (minPoints > 0 && (stat.Points ?? 0) < minPoints) continue;
            if (minAssists > 0 && (stat.Assists ?? 0) < minAssists) continue;
            if (minRebounds > 0 && (stat.Rebounds ?? 0) < minRebounds) continue;
            if (minPir > 0 && (stat.Pir ?? 0) < minPir) continue;
            if (minFoulsDrawn > 0 && (stat.FoulsDrawn ?? 0) < minFoulsDrawn) continue;

            if (maxAge.HasValue)
            {
                int? playerAge = player.CurrentAge;
                if (!playerAge.HasValue || playerAge.Value > maxAge.Value) continue;
            }

            if (minHeightCm.HasValue)
            {
                if (!player.HeightCm.HasValue || player.HeightCm.Value < minHeightCm.Value) continue;
            }

            if (targetContractYear.HasValue)
            {
                if (!player.ContractExpirySeason.HasValue || player.ContractExpirySeason.Value > targetContractYear.Value) continue;
            }

            if (minFg3Pct > 0m && (stat.Fg3Pct ?? 0m) < minFg3Pct) continue;
            if (minTsPct > 0m && (stat.TrueShootingPct ?? 0m) < minTsPct) continue;

            var archetype = _analyticsService.DetermineArchetype(stat, player.Position, player.Height);
            var radar = _analyticsService.CalculateRadarMetrics(stat);
            double eci = _analyticsService.CalculateEuropeanCompatibilityIndex(stat);
            double dpi = _analyticsService.CalculateDefensivePressureIndex(stat);
            double buyLow = _analyticsService.CalculateBuyLowBounceBackIndex(stat, careerHistory);

            double score = 40.0;
            var reasons = new List<string>();

            // Archetype Evaluation
            if (targetArchetype.HasValue)
            {
                if (archetype == targetArchetype.Value)
                {
                    score += 35.0;
                    reasons.Add($"Exact Archetype Match: {archetype}");
                }
                else
                {
                    score -= 15.0;
                }
            }

            // Scoring Factor
            if (wantsScoring)
            {
                double scoringBonus = Math.Min(25.0, ((double)(stat.Points ?? 0) / 30.0) * 25.0);
                score += scoringBonus;
                if ((stat.Points ?? 0) >= 20.0m)
                    reasons.Add($"High-Volume Scoring: {stat.Points:F1} PPG");
            }

            // 3-Point Shooting Factor
            if (wantsShooting)
            {
                decimal fg3 = stat.Fg3Pct ?? 0m;
                double shootBonus = Math.Min(25.0, ((double)fg3 / 0.40) * 25.0);
                score += shootBonus;
                if (fg3 >= 0.38m)
                    reasons.Add($"Elite 3-Point Shooting: {fg3 * 100:F1}% 3P");
            }

            // Efficiency Factor
            if (wantsEfficiency)
            {
                decimal ts = stat.TrueShootingPct ?? 0.50m;
                double effBonus = Math.Max(0.0, Math.Min(25.0, (((double)ts - 0.48) / 0.18) * 25.0));
                score += effBonus;
                if (ts >= 0.58m)
                    reasons.Add($"Elite Shooting Efficiency: {ts * 100:F1}% True Shooting");
            }

            // Playmaking Factor
            if (wantsPlaymaking)
            {
                double playBonus = Math.Min(25.0, ((double)(stat.Assists ?? 0) / 9.0) * 25.0);
                score += playBonus;
                if ((stat.Assists ?? 0) >= 6.0m)
                    reasons.Add($"Primary Playmaker: {stat.Assists:F1} APG");
            }

            // Rebounding Factor
            if (wantsRebounding)
            {
                double rebBonus = Math.Min(20.0, ((double)(stat.Rebounds ?? 0) / 12.0) * 20.0);
                score += rebBonus;
                if ((stat.Rebounds ?? 0) >= 8.5m)
                    reasons.Add($"Dominant Rebounding: {stat.Rebounds:F1} RPG");
            }

            // Defense Factor
            if (wantsDefense)
            {
                double defBonus = Math.Min(20.0, (((double)(stat.Steals ?? 0) * 1.5 + (double)(stat.Blocks ?? 0) * 1.5) / 4.0) * 20.0);
                score += defBonus;
                if ((stat.Steals ?? 0) >= 1.2m || (stat.Blocks ?? 0) >= 1.2m)
                    reasons.Add($"Defensive Impact: {stat.Steals:F1} STL, {stat.Blocks:F1} BLK");
            }

            // Beşiktaş / Dušan Alimpijević System (High-Pressure Perimeter Disruption)
            if (wantsBesiktasStyle)
            {
                double besiktasBonus = Math.Min(35.0, (dpi / 100.0) * 35.0);
                score += besiktasBonus;
                if (dpi >= 65.0)
                    reasons.Add($"Alimpijević System Fit: Defensive Pressure Index {dpi:F1}/100 (Disruptive Perimeter Press, {stat.Steals:F1} STL)");
            }

            // European Compatibility Index (ECI)
            if (wantsEuropeanFit)
            {
                double eciBonus = Math.Min(30.0, (eci / 100.0) * 30.0);
                score += eciBonus;
                if (eci >= 65.0)
                    reasons.Add($"European Compatibility: ECI {eci:F1}/100 (Tactical IQ, Spacing & Low Turnovers)");
            }

            // Moneyball Sleeper / Buy-Low / Bounce-Back Factor
            if (wantsBuyLowSleeper)
            {
                double sleeperBonus = Math.Min(40.0, (buyLow / 100.0) * 40.0);
                score += sleeperBonus;

                // Prevent established top-tier starters/All-Stars, aging veterans, stale years, or micro-samples from dominating Buy-Low queries
                bool isEstablishedTopTierStar = (stat.LeagueDifficultyCoefficient >= 0.86m && (stat.Minutes ?? 0m) >= 24.0m && stat.GamesPlayed >= 15)
                    || (careerHistory != null && careerHistory.Any(s =>
                        s.Season >= stat.Season - 1 && s.Season <= stat.Season &&
                        s.LeagueDifficultyCoefficient >= 0.86m && s.GamesPlayed >= 25 && ((s.Points ?? 0m) >= 16.0m || (s.Minutes ?? 0m) >= 27.0m)));
                if (isEstablishedTopTierStar)
                {
                    score -= 24.0;
                }
                if (!targetSeason.HasValue && stat.Season < maxDatasetSeason - 2)
                {
                    score -= 12.0;
                }
                if (player.CurrentAge.HasValue && player.CurrentAge.Value >= 31)
                {
                    score -= (player.CurrentAge.Value - 30) * 5.0;
                }
                if (stat.GamesPlayed < 8 || (stat.Minutes ?? 0m) < 8.0m)
                {
                    score -= 15.0;
                }

                var priorPeak = careerHistory?
                    .Where(s => s.Season < stat.Season && s.Season >= stat.Season - 3)
                    .OrderByDescending(s => (s.Points ?? 0m) + (s.Pir ?? s.PlayerEfficiencyRating ?? 0m) * 0.5m)
                    .FirstOrDefault();

                if (priorPeak != null && (priorPeak.Points ?? 0m) > (stat.Points ?? 0m) + 1.5m)
                {
                    string peakLeague = !string.IsNullOrWhiteSpace(priorPeak.League) ? priorPeak.League : statLeague;
                    reasons.Add($"Uyuyan Dev / Buy-Low Target (Index: {buyLow:F1}/100): Peak in {priorPeak.Season} ({priorPeak.Points:F1} PPG in {peakLeague}) → Currently {stat.Points:F1} PPG in {stat.Minutes:F1} MIN ({stat.PointsPer40:F1} PTS/40)");
                }
                else if (buyLow >= 60.0)
                {
                    reasons.Add($"Per-40 Breakout Gem (Index: {buyLow:F1}/100): Producing {stat.PointsPer40:F1} PTS/40 in {stat.Minutes:F1} MPG");
                }
            }

            // Valuation / Performance Index Rating (PIR)
            if (wantsPir)
            {
                double pirVal = (double)(stat.Pir ?? 10m);
                double pirBonus = Math.Min(25.0, (pirVal / 18.0) * 25.0);
                score += pirBonus;
                if ((stat.Pir ?? 0) >= 14m)
                    reasons.Add($"High Valuation / PIR: {stat.Pir:F1} Rating");
            }

            // Fouls Drawn Factor
            if (wantsFoulsDrawn)
            {
                double fdVal = (double)(stat.FoulsDrawn ?? 2m);
                double fdBonus = Math.Min(20.0, (fdVal / 4.5) * 20.0);
                score += fdBonus;
                if ((stat.FoulsDrawn ?? 0) >= 3.0m)
                    reasons.Add($"Physical Contact Magnet: {stat.FoulsDrawn:F1} Fouls Drawn");
            }

            // Domestic Passport
            if (wantsTurkish)
            {
                score += 20.0;
                reasons.Add("Domestic Passport Eligible: Turkish National");
            }

            // Multi-Constraint Bonuses (Low Turnover, Low Foul, Off-Ball, Switchability, Contract, Age/Size)
            if (wantsLowTurnover)
            {
                decimal astTov = stat.AssistToTurnoverRatio ?? 1.5m;
                decimal tov = stat.Turnovers ?? 1.8m;
                if (astTov >= 2.0m || tov <= 1.5m)
                {
                    score += 22.0;
                    reasons.Add($"High Ball Security: {astTov:F2} AST/TOV ({tov:F1} TOV/G)");
                }
                else if (tov >= 2.8m)
                {
                    score -= 18.0;
                }
            }

            if (wantsLowFoul)
            {
                decimal pf36 = (stat.Minutes > 0 && stat.PersonalFoulsCommitted.HasValue)
                    ? (stat.PersonalFoulsCommitted.Value / stat.Minutes.Value) * 36m
                    : 2.8m;
                if (pf36 <= 3.1m)
                {
                    score += 18.0;
                    reasons.Add($"High FIBA Foul Discipline: {pf36:F1} PF/36");
                }
            }

            if (wantsLowUsage)
            {
                decimal usg = stat.UsagePct ?? 18m;
                if (usg <= 20.5m && (stat.TrueShootingPct ?? 0m) >= 0.55m)
                {
                    score += 20.0;
                    reasons.Add($"Scalable Low-Usage Plug-and-Play: {usg:F1}% USG, {(stat.TrueShootingPct ?? 0) * 100:F1}% TS");
                }
            }

            if (wantsSwitchable)
            {
                decimal stocks36 = stat.StocksPer36 ?? ((stat.StlPer36 ?? 0m) + (stat.BlkPer36 ?? 0m));
                int plusLen = (player.WingspanCm ?? 0) - (player.HeightCm ?? 0);
                if (stocks36 >= 1.8m || plusLen >= 8)
                {
                    score += 18.0;
                    reasons.Add($"Switchable Positional Length: {player.HeightCm}cm / {player.WingspanCm}cm WS ({stocks36:F1} Stocks/36)");
                }
            }

            if (maxAge.HasValue && player.CurrentAge.HasValue)
            {
                reasons.Add($"Age Criteria Verified: {player.CurrentAge} yrs (≤ U{maxAge.Value})");
            }

            if (minHeightCm.HasValue && player.HeightCm.HasValue)
            {
                reasons.Add($"Size Verified: {player.HeightCm} cm (WS: {player.WingspanCm ?? player.HeightCm + 7} cm)");
            }

            if (targetContractYear.HasValue && player.ContractExpirySeason.HasValue)
            {
                reasons.Add($"Contract Target: Expires {player.ContractExpirySeason} ({player.EstimatedMarketValueTier ?? "Mid-Tier"})");
            }

            // Overall Impact / Star Quality
            if (wantsHighImpact)
            {
                score += (radar.OverallImpact / 100.0) * 20.0;
                if (radar.OverallImpact >= 75.0)
                    reasons.Add($"All-NBA Caliber Impact: {radar.OverallImpact:F1}/100 Impact Score");
            }

            // If no specific metric was specified, boost by overall impact
            if (!wantsScoring && !wantsShooting && !wantsEfficiency && !wantsPlaymaking && !wantsRebounding && 
                !wantsDefense && !wantsBesiktasStyle && !wantsEuropeanFit && !wantsBuyLowSleeper && !wantsPir && !targetArchetype.HasValue)
            {
                score += (radar.OverallImpact / 100.0) * 30.0;
                reasons.Add($"Overall Production Index: {radar.OverallImpact:F1}/100");
            }

            // Smooth asymptotic score compression above 78.0 avoids multi-player saturation ties at 99.9
            double finalScore = score <= 78.0
                ? Math.Round(Math.Max(10.0, score), 1)
                : Math.Round(Math.Min(99.5, 78.0 + 21.5 * (1.0 - Math.Exp(-(score - 78.0) / 28.0))), 1);

            matchedResults.Add(new SmartScoutSearchResultDto
            {
                PlayerId = player.Id,
                FirstName = player.FirstName,
                LastName = player.LastName,
                TeamName = statTeam,
                Position = player.Position,
                Season = stat.Season,
                Archetype = archetype.ToString(),
                FitScore = finalScore,
                MatchReasons = reasons.Any() ? reasons : new List<string> { $"Fits tactical profile: {archetype}" },
                ParsedConstraints = parsedConstraints,
                Age = player.CurrentAge,
                HeightCm = player.HeightCm,
                ContractExpirySeason = player.ContractExpirySeason,
                EstimatedMarketValueTier = player.EstimatedMarketValueTier,
                EuroLeagueTransitionIndex = stat.EuroLeagueTransitionIndex,
                StocksPer36 = stat.StocksPer36 ?? ((stat.StlPer36 ?? 0m) + (stat.BlkPer36 ?? 0m)),
                Points = stat.Points,
                Rebounds = stat.Rebounds,
                Assists = stat.Assists,
                Steals = stat.Steals,
                Blocks = stat.Blocks,
                PtsPer36 = stat.PtsPer36,
                RebPer36 = stat.RebPer36,
                AstPer36 = stat.AstPer36,
                TrueShootingPct = stat.TrueShootingPct,
                EffectiveFgPct = stat.EffectiveFgPct,
                AssistToTurnoverRatio = stat.AssistToTurnoverRatio,
                GameScoreAverage = stat.GameScoreAverage,
                PlayerEfficiencyRating = stat.PlayerEfficiencyRating,
                UsagePct = stat.UsagePct,
                WinShares = stat.WinShares,
                BoxPlusMinus = stat.BoxPlusMinus,
                Pir = stat.Pir,
                FoulsDrawn = stat.FoulsDrawn,
                PointsPer40 = stat.PointsPer40,
                Nationality = player.Nationality,
                League = statLeague,
                LeagueDifficultyCoefficient = stat.LeagueDifficultyCoefficient,
                EuropeanCompatibilityIndex = eci,
                DefensivePressureIndex = dpi,
                BuyLowBounceBackIndex = buyLow,
                RadarMetrics = radar
            });
        }

        // Deduplicate by PlayerId (keep each player's highest-scoring season profile)
        return matchedResults
            .GroupBy(r => r.PlayerId)
            .Select(g => g.OrderByDescending(r => r.FitScore).ThenByDescending(r => r.Season).First())
            .OrderByDescending(r => r.FitScore)
            .Take(top)
            .ToList();
    }

    private static PlayerArchetype? DetectArchetype(string prompt)
    {
        if (prompt.Contains("point center") || prompt.Contains("playmaking center") || prompt.Contains("passing big") || prompt.Contains("jokic"))
            return PlayerArchetype.PointCenter;
        if (prompt.Contains("paint anchor") || prompt.Contains("rim protect") || prompt.Contains("shot block") || prompt.Contains("rim defense"))
            return PlayerArchetype.PaintAnchor;
        if (prompt.Contains("sharpshooter") || prompt.Contains("sniper") || prompt.Contains("3pt") || prompt.Contains("floor spac"))
            return PlayerArchetype.Sharpshooter;
        if (prompt.Contains("two way") || prompt.Contains("two-way") || prompt.Contains("lockdown"))
            return PlayerArchetype.TwoWayStar;
        if (prompt.Contains("floor general") || prompt.Contains("pass first") || prompt.Contains("playmaker"))
            return PlayerArchetype.FloorGeneral;
        if (prompt.Contains("stretch big") || prompt.Contains("stretch 4") || prompt.Contains("stretch 5"))
            return PlayerArchetype.StretchBig;
        if (prompt.Contains("slasher") || prompt.Contains("slashing wing"))
            return PlayerArchetype.SlashingWing;

        return null;
    }

    private static string? DetectPosition(string prompt)
    {
        if (prompt.Contains("center") || prompt.Contains("pivot") || prompt.Contains("big man") || prompt.Contains("bigs") || prompt.Contains("5 numara") || prompt.Contains("uzun"))
            return "C";
        if (prompt.Contains("guard") || prompt.Contains("point guard") || prompt.Contains("shooting guard") || prompt.Contains("pg") || prompt.Contains("sg") || prompt.Contains("oyun kurucu") || prompt.Contains("kısa") || prompt.Contains("kisa"))
            return "G";
        if (prompt.Contains("forward") || prompt.Contains("wing") || prompt.Contains("small forward") || prompt.Contains("power forward") || prompt.Contains("sf") || prompt.Contains("pf") || prompt.Contains("forvet") || prompt.Contains("kanat"))
            return "F";

        return null;
    }

    private static decimal ExtractMetricThreshold(string prompt, string regexPattern)
    {
        var match = Regex.Match(prompt, regexPattern);
        if (match.Success && decimal.TryParse(match.Groups[1].Value, out decimal val))
        {
            return val;
        }
        return 0m;
    }

    private static int? ExtractAgeCap(string prompt)
    {
        var m1 = Regex.Match(prompt, @"\bu\s*(1[89]|2[0-9]|3[0-5])\b");
        if (m1.Success && int.TryParse(m1.Groups[1].Value, out int uAge)) return uAge;

        var m2 = Regex.Match(prompt, @"(?:under|max age|below)\s*(1[89]|2[0-9]|3[0-5])\b");
        if (m2.Success && int.TryParse(m2.Groups[1].Value, out int underAge)) return underAge;

        var m3 = Regex.Match(prompt, @"\b(1[89]|2[0-9]|3[0-5])\s*(?:yaş altı|yas alti|yaşından küçük|yasindan kucuk)");
        if (m3.Success && int.TryParse(m3.Groups[1].Value, out int trAge)) return trAge;

        return null;
    }

    private static int? ExtractMinHeightCm(string prompt)
    {
        var m = Regex.Match(prompt, @"\b(1[89]\d|2[012]\d)\+?\s*cm\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int cm)) return cm;
        return null;
    }

    private static int? ExtractContractExpiryYear(string prompt)
    {
        if (prompt.Contains("contract") || prompt.Contains("expir") || prompt.Contains("free agent") ||
            prompt.Contains("kontrat") || prompt.Contains("sözleşme") || prompt.Contains("sozlesme") || prompt.Contains("biten"))
        {
            var m = Regex.Match(prompt, @"\b(202[5-9])\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int yr)) return yr;
        }
        return null;
    }

    private static decimal ExtractPercentageCutoff(string prompt, string pattern)
    {
        var m = Regex.Match(prompt, pattern);
        if (!m.Success) return 0m;
        string raw = !string.IsNullOrEmpty(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
        if (decimal.TryParse(raw, out decimal pct) && pct > 0 && pct <= 99)
        {
            return pct / 100m;
        }
        return 0m;
    }

    private static List<string> BuildParsedConstraintsList(
        string? pos, PlayerArchetype? arch, int? maxAge, int? minHeightCm, int? contractYear,
        decimal minPts, decimal minAst, decimal minReb, decimal minFg3, decimal minTs,
        bool turkish, bool lowTov, bool lowFoul, bool lowUsg, bool switchable,
        bool besiktas, bool euroFit, bool buyLow)
    {
        var list = new List<string>();
        if (!string.IsNullOrEmpty(pos)) list.Add($"Position: {pos}");
        if (arch.HasValue) list.Add($"Archetype: {arch.Value}");
        if (maxAge.HasValue) list.Add($"Age ≤ U{maxAge.Value}");
        if (minHeightCm.HasValue) list.Add($"Height ≥ {minHeightCm.Value}cm");
        if (contractYear.HasValue) list.Add($"Contract ≤ {contractYear.Value}");
        if (minPts > 0) list.Add($"PTS ≥ {minPts}");
        if (minAst > 0) list.Add($"AST ≥ {minAst}");
        if (minReb > 0) list.Add($"REB ≥ {minReb}");
        if (minFg3 > 0) list.Add($"3P% ≥ {minFg3 * 100:0}%");
        if (minTs > 0) list.Add($"TS% ≥ {minTs * 100:0}%");
        if (turkish) list.Add("Passport: TUR (Domestic)");
        if (lowTov) list.Add("Discipline: High AST/TOV");
        if (lowFoul) list.Add("FIBA Foul Safe");
        if (lowUsg) list.Add("Role: Low-Usage / Off-Ball");
        if (switchable) list.Add("Defense: Switchable Length");
        if (besiktas) list.Add("System: High-Press DPI");
        if (euroFit) list.Add("System: Euro Half-Court ECI");
        if (buyLow) list.Add("Market: Buy-Low / Sleeper");
        return list;
    }
}
