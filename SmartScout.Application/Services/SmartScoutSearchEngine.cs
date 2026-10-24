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
        int top = 20)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return new List<SmartScoutSearchResultDto>();

        string cleanPrompt = prompt.ToLowerInvariant();

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

        // European & Beşiktaş Tactical Intent
        bool wantsBesiktasStyle = cleanPrompt.Contains("besiktas") || cleanPrompt.Contains("beşiktaş") || cleanPrompt.Contains("bjk") ||
                                  cleanPrompt.Contains("alimpijevic") || cleanPrompt.Contains("alimpijević") || cleanPrompt.Contains("tam saha pres") ||
                                  cleanPrompt.Contains("ball pressure") || cleanPrompt.Contains("press") || cleanPrompt.Contains("disrupt");
        bool wantsEuropeanFit = cleanPrompt.Contains("euro") || cleanPrompt.Contains("avrupa") || cleanPrompt.Contains("eci") ||
                               cleanPrompt.Contains("half court") || cleanPrompt.Contains("spacing") || cleanPrompt.Contains("fiba");
        bool wantsPir = cleanPrompt.Contains("pir") || cleanPrompt.Contains("valuation") || cleanPrompt.Contains("verimlilik");
        bool wantsFoulsDrawn = cleanPrompt.Contains("foul") || cleanPrompt.Contains("faul") || cleanPrompt.Contains("contact");
        bool wantsTurkish = cleanPrompt.Contains("turk") || cleanPrompt.Contains("türk") || cleanPrompt.Contains("yerli");

        bool wantsEuroLeagueOnly = cleanPrompt.Contains("euroleague") && !cleanPrompt.Contains("eurocup");
        bool wantsEuroCupOnly = cleanPrompt.Contains("eurocup");
        bool wantsNbaOnly = cleanPrompt.Contains("nba");

        // 4. Extract explicit numeric thresholds (e.g., "20+ pts", "25 pts")
        decimal minPoints = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:pts|points)");
        decimal minAssists = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:ast|assists)");
        decimal minRebounds = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:reb|rebounds)");
        decimal minPir = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:pir|valuation|verimlilik)");
        decimal minFoulsDrawn = ExtractMetricThreshold(cleanPrompt, @"(\d+)\+?\s*(?:fouls?|faul)");

        var matchedResults = new List<SmartScoutSearchResultDto>();

        foreach (var stat in candidateStats)
        {
            var player = stat.Player;
            if (player == null) continue;

            // Strict Filter 1: Games played minimum sample
            if (stat.GamesPlayed < 10) continue;

            // Strict Filter 2: Explicit position conflict
            if (targetPosition != null && !string.IsNullOrEmpty(player.Position))
            {
                if (targetPosition == "C" && !player.Position.Contains('C') && !player.Position.Contains('F')) continue;
                if (targetPosition == "G" && !player.Position.Contains('G')) continue;
                if (targetPosition == "F" && !player.Position.Contains('F') && !player.Position.Contains('G')) continue;
            }

            // Strict Filter 3: Competition / League restrictions
            if (wantsEuroLeagueOnly && !string.Equals(player.League, "EuroLeague", StringComparison.OrdinalIgnoreCase)) continue;
            if (wantsEuroCupOnly && !string.Equals(player.League, "EuroCup", StringComparison.OrdinalIgnoreCase)) continue;
            if (wantsNbaOnly && !string.Equals(player.League, "NBA", StringComparison.OrdinalIgnoreCase)) continue;

            // Strict Filter 4: Domestic Turkish Passport
            if (wantsTurkish)
            {
                bool isTurkish = string.Equals(player.Nationality, "TUR", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(player.Nationality, "Turkey", StringComparison.OrdinalIgnoreCase);
                if (!isTurkish) continue;
            }

            // Strict Filter 5: Explicit numeric thresholds
            if (minPoints > 0 && (stat.Points ?? 0) < minPoints) continue;
            if (minAssists > 0 && (stat.Assists ?? 0) < minAssists) continue;
            if (minRebounds > 0 && (stat.Rebounds ?? 0) < minRebounds) continue;
            if (minPir > 0 && (stat.Pir ?? 0) < minPir) continue;
            if (minFoulsDrawn > 0 && (stat.FoulsDrawn ?? 0) < minFoulsDrawn) continue;

            var archetype = _analyticsService.DetermineArchetype(stat, player.Position, player.Height);
            var radar = _analyticsService.CalculateRadarMetrics(stat);
            double eci = _analyticsService.CalculateEuropeanCompatibilityIndex(stat);
            double dpi = _analyticsService.CalculateDefensivePressureIndex(stat);

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
                    // Partial mismatch penalty
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
                reasons.Add("Domestic Passport Eligible: Turkish National (Yerli Statüsü)");
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
                !wantsDefense && !wantsBesiktasStyle && !wantsEuropeanFit && !wantsPir && !targetArchetype.HasValue)
            {
                score += (radar.OverallImpact / 100.0) * 30.0;
                reasons.Add($"Overall Production Index: {radar.OverallImpact:F1}/100");
            }

            double finalScore = Math.Round(Math.Max(10.0, Math.Min(99.9, score)), 1);

            matchedResults.Add(new SmartScoutSearchResultDto
            {
                PlayerId = player.Id,
                FirstName = player.FirstName,
                LastName = player.LastName,
                TeamName = player.TeamName,
                Position = player.Position,
                Season = stat.Season,
                Archetype = archetype.ToString(),
                FitScore = finalScore,
                MatchReasons = reasons.Any() ? reasons : new List<string> { $"Fits tactical profile: {archetype}" },
                Points = stat.Points,
                Rebounds = stat.Rebounds,
                Assists = stat.Assists,
                Steals = stat.Steals,
                Blocks = stat.Blocks,
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
                EuropeanCompatibilityIndex = eci,
                DefensivePressureIndex = dpi,
                RadarMetrics = radar
            });
        }

        return matchedResults
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
        if (prompt.Contains("center") || prompt.Contains("pivot") || prompt.Contains("big man") || prompt.Contains("bigs"))
            return "C";
        if (prompt.Contains("guard") || prompt.Contains("point guard") || prompt.Contains("shooting guard") || prompt.Contains("pg") || prompt.Contains("sg"))
            return "G";
        if (prompt.Contains("forward") || prompt.Contains("wing") || prompt.Contains("small forward") || prompt.Contains("power forward") || prompt.Contains("sf") || prompt.Contains("pf"))
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
}
