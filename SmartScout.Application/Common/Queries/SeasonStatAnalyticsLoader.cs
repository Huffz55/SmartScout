using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Common.Queries;

/// <summary>
/// Shared analytics query projection loader that reads only scalar columns required by
/// scouting engines while omitting heavy jsonb/text columns (RawScrapedPayload, ExternalIdsJson, ScoutNotes)
/// and deduplicating hydrated Player instances in memory.
/// </summary>
public static class SeasonStatAnalyticsLoader
{
    public sealed class AnalyticsRowProjection
    {
        public Guid Id { get; init; }
        public Guid PlayerId { get; init; }
        public int Season { get; init; }
        public string League { get; init; } = "NBA";
        public string TeamName { get; init; } = "TOT";
        public string CompetitionPhase { get; init; } = "Regular Season";
        public decimal LeagueDifficultyCoefficient { get; init; }
        public int GamesPlayed { get; init; }
        public int? GamesStarted { get; init; }
        public decimal? MinutesPlayedTotal { get; init; }
        public decimal? Minutes { get; init; }
        public decimal? Points { get; init; }
        public decimal? FieldGoalsMade { get; init; }
        public decimal? FieldGoalsAttempted { get; init; }
        public decimal? FgPct { get; init; }
        public decimal? ThreePointsMade { get; init; }
        public decimal? ThreePointsAttempted { get; init; }
        public decimal? Fg3Pct { get; init; }
        public decimal? TwoPointsMade { get; init; }
        public decimal? TwoPointsAttempted { get; init; }
        public decimal? Fg2Pct { get; init; }
        public decimal? FreeThrowsMade { get; init; }
        public decimal? FreeThrowsAttempted { get; init; }
        public decimal? FtPct { get; init; }
        public decimal? OffensiveRebounds { get; init; }
        public decimal? DefensiveRebounds { get; init; }
        public decimal? Rebounds { get; init; }
        public decimal? Assists { get; init; }
        public decimal? Steals { get; init; }
        public decimal? Blocks { get; init; }
        public decimal? Turnovers { get; init; }
        public decimal? PersonalFoulsCommitted { get; init; }
        public decimal? FoulsDrawn { get; init; }
        public decimal? PlusMinus { get; init; }
        public decimal? PtsPer36 { get; init; }
        public decimal? RebPer36 { get; init; }
        public decimal? AstPer36 { get; init; }
        public decimal? StlPer36 { get; init; }
        public decimal? BlkPer36 { get; init; }
        public decimal? TovPer36 { get; init; }
        public decimal? PointsPer40 { get; init; }
        public decimal? AssistsPer40 { get; init; }
        public decimal? ReboundsPer40 { get; init; }
        public decimal? TrueShootingPct { get; init; }
        public decimal? EffectiveFgPct { get; init; }
        public decimal? UsagePct { get; init; }
        public decimal? AssistToTurnoverRatio { get; init; }
        public decimal? GameScoreAverage { get; init; }
        public decimal? Pir { get; init; }
        public decimal? PlayerEfficiencyRating { get; init; }
        public decimal? TotalReboundPct { get; init; }
        public decimal? AssistPct { get; init; }
        public decimal? WinShares { get; init; }
        public decimal? BoxPlusMinus { get; init; }
        public decimal? StocksPer36 { get; init; }
        public decimal? ThreePointRate { get; init; }
        public decimal? EuroLeagueTransitionIndex { get; init; }

        public int PlayerExternalApiId { get; init; }
        public string PlayerFirstName { get; init; } = string.Empty;
        public string PlayerLastName { get; init; } = string.Empty;
        public string PlayerFullName { get; init; } = string.Empty;
        public string PlayerNormalizedName { get; init; } = string.Empty;
        public string? PlayerPosition { get; init; }
        public string? PlayerPrimaryPosition { get; init; }
        public string? PlayerSecondaryPosition { get; init; }
        public string? PlayerHeight { get; init; }
        public int? PlayerHeightCm { get; init; }
        public int? PlayerWeightPounds { get; init; }
        public int? PlayerWeightKg { get; init; }
        public int? PlayerWingspanCm { get; init; }
        public int? PlayerStandingReachCm { get; init; }
        public int? PlayerDraftYear { get; init; }
        public int? PlayerDraftPick { get; init; }
        public string? PlayerTeamName { get; init; }
        public string PlayerLeague { get; init; } = string.Empty;
        public string? PlayerNationality { get; init; }
        public DateTime? PlayerBirthDate { get; init; }
        public int? PlayerContractExpirySeason { get; init; }
        public string? PlayerEstimatedMarketValueTier { get; init; }
        public string? PlayerAgencyName { get; init; }
        public string PlayerDominantHand { get; init; } = "Right";
        public bool PlayerIsWatchlisted { get; init; }
    }

    public static IQueryable<AnalyticsRowProjection> BuildAnalyticsQuery(IQueryable<PlayerSeasonStat> source) =>
        source
            .Where(s => s.Player != null)
            .Select(s => new AnalyticsRowProjection
            {
                Id = s.Id,
                PlayerId = s.PlayerId,
                Season = s.Season,
                League = s.League,
                TeamName = s.TeamName,
                CompetitionPhase = s.CompetitionPhase,
                LeagueDifficultyCoefficient = s.LeagueDifficultyCoefficient,
                GamesPlayed = s.GamesPlayed,
                GamesStarted = s.GamesStarted,
                MinutesPlayedTotal = s.MinutesPlayedTotal,
                Minutes = s.Minutes,
                Points = s.Points,
                FieldGoalsMade = s.FieldGoalsMade,
                FieldGoalsAttempted = s.FieldGoalsAttempted,
                FgPct = s.FgPct,
                ThreePointsMade = s.ThreePointsMade,
                ThreePointsAttempted = s.ThreePointsAttempted,
                Fg3Pct = s.Fg3Pct,
                TwoPointsMade = s.TwoPointsMade,
                TwoPointsAttempted = s.TwoPointsAttempted,
                Fg2Pct = s.Fg2Pct,
                FreeThrowsMade = s.FreeThrowsMade,
                FreeThrowsAttempted = s.FreeThrowsAttempted,
                FtPct = s.FtPct,
                OffensiveRebounds = s.OffensiveRebounds,
                DefensiveRebounds = s.DefensiveRebounds,
                Rebounds = s.Rebounds,
                Assists = s.Assists,
                Steals = s.Steals,
                Blocks = s.Blocks,
                Turnovers = s.Turnovers,
                PersonalFoulsCommitted = s.PersonalFoulsCommitted,
                FoulsDrawn = s.FoulsDrawn,
                PlusMinus = s.PlusMinus,
                PtsPer36 = s.PtsPer36,
                RebPer36 = s.RebPer36,
                AstPer36 = s.AstPer36,
                StlPer36 = s.StlPer36,
                BlkPer36 = s.BlkPer36,
                TovPer36 = s.TovPer36,
                PointsPer40 = s.PointsPer40,
                AssistsPer40 = s.AssistsPer40,
                ReboundsPer40 = s.ReboundsPer40,
                TrueShootingPct = s.TrueShootingPct,
                EffectiveFgPct = s.EffectiveFgPct,
                UsagePct = s.UsagePct,
                AssistToTurnoverRatio = s.AssistToTurnoverRatio,
                GameScoreAverage = s.GameScoreAverage,
                Pir = s.Pir,
                PlayerEfficiencyRating = s.PlayerEfficiencyRating,
                TotalReboundPct = s.TotalReboundPct,
                AssistPct = s.AssistPct,
                WinShares = s.WinShares,
                BoxPlusMinus = s.BoxPlusMinus,
                StocksPer36 = s.StocksPer36,
                ThreePointRate = s.ThreePointRate,
                EuroLeagueTransitionIndex = s.EuroLeagueTransitionIndex,

                PlayerExternalApiId = s.Player!.ExternalApiId,
                PlayerFirstName = s.Player.FirstName,
                PlayerLastName = s.Player.LastName,
                PlayerFullName = s.Player.FullName,
                PlayerNormalizedName = s.Player.NormalizedName,
                PlayerPosition = s.Player.Position,
                PlayerPrimaryPosition = s.Player.PrimaryPosition,
                PlayerSecondaryPosition = s.Player.SecondaryPosition,
                PlayerHeight = s.Player.Height,
                PlayerHeightCm = s.Player.HeightCm,
                PlayerWeightPounds = s.Player.WeightPounds,
                PlayerWeightKg = s.Player.WeightKg,
                PlayerWingspanCm = s.Player.WingspanCm,
                PlayerStandingReachCm = s.Player.StandingReachCm,
                PlayerDraftYear = s.Player.DraftYear,
                PlayerDraftPick = s.Player.DraftPick,
                PlayerTeamName = s.Player.TeamName,
                PlayerLeague = s.Player.League,
                PlayerNationality = s.Player.Nationality,
                PlayerBirthDate = s.Player.BirthDate,
                PlayerContractExpirySeason = s.Player.ContractExpirySeason,
                PlayerEstimatedMarketValueTier = s.Player.EstimatedMarketValueTier,
                PlayerAgencyName = s.Player.AgencyName,
                PlayerDominantHand = s.Player.DominantHand,
                PlayerIsWatchlisted = s.Player.IsWatchlisted
            });

    public static async Task<List<PlayerSeasonStat>> LoadProjectedStatsAsync(
        ISmartScoutDbContext dbContext,
        Expression<Func<PlayerSeasonStat, bool>>? predicate = null,
        bool linkCareerHistory = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<PlayerSeasonStat> query = dbContext.SeasonStats.AsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }

        var rows = await BuildAnalyticsQuery(query).ToListAsync(cancellationToken);
        return HydrateRows(rows, linkCareerHistory);
    }

    internal static List<PlayerSeasonStat> HydrateRows(IReadOnlyList<AnalyticsRowProjection> rows, bool linkCareerHistory)
    {
        var result = new List<PlayerSeasonStat>(rows.Count);
        var playersById = new Dictionary<Guid, Player>();

        foreach (var r in rows)
        {
            if (!playersById.TryGetValue(r.PlayerId, out var player))
            {
                player = Player.FromAnalyticsProjection(
                    r.PlayerId,
                    r.PlayerExternalApiId,
                    r.PlayerFirstName,
                    r.PlayerLastName,
                    r.PlayerFullName,
                    r.PlayerNormalizedName,
                    r.PlayerPosition,
                    r.PlayerPrimaryPosition,
                    r.PlayerSecondaryPosition,
                    r.PlayerHeight,
                    r.PlayerHeightCm,
                    r.PlayerWeightPounds,
                    r.PlayerWeightKg,
                    r.PlayerWingspanCm,
                    r.PlayerStandingReachCm,
                    r.PlayerDraftYear,
                    r.PlayerDraftPick,
                    r.PlayerTeamName,
                    r.PlayerLeague,
                    r.PlayerNationality,
                    r.PlayerBirthDate,
                    r.PlayerContractExpirySeason,
                    r.PlayerEstimatedMarketValueTier,
                    r.PlayerAgencyName,
                    r.PlayerDominantHand,
                    r.PlayerIsWatchlisted);
                playersById[r.PlayerId] = player;
            }

            var stat = PlayerSeasonStat.FromAnalyticsProjection(
                r.Id, r.PlayerId, r.Season, r.League, r.TeamName, r.CompetitionPhase,
                r.LeagueDifficultyCoefficient, r.GamesPlayed, r.GamesStarted, r.MinutesPlayedTotal, r.Minutes,
                r.Points, r.FieldGoalsMade, r.FieldGoalsAttempted, r.FgPct,
                r.ThreePointsMade, r.ThreePointsAttempted, r.Fg3Pct,
                r.TwoPointsMade, r.TwoPointsAttempted, r.Fg2Pct,
                r.FreeThrowsMade, r.FreeThrowsAttempted, r.FtPct,
                r.OffensiveRebounds, r.DefensiveRebounds, r.Rebounds,
                r.Assists, r.Steals, r.Blocks, r.Turnovers,
                r.PersonalFoulsCommitted, r.FoulsDrawn, r.PlusMinus,
                r.PtsPer36, r.RebPer36, r.AstPer36, r.StlPer36, r.BlkPer36, r.TovPer36,
                r.PointsPer40, r.AssistsPer40, r.ReboundsPer40,
                r.TrueShootingPct, r.EffectiveFgPct, r.UsagePct, r.AssistToTurnoverRatio, r.GameScoreAverage,
                r.Pir, r.PlayerEfficiencyRating, r.TotalReboundPct, r.AssistPct, r.WinShares, r.BoxPlusMinus,
                r.StocksPer36, r.ThreePointRate, r.EuroLeagueTransitionIndex);

            stat.AttachPlayer(player);
            if (linkCareerHistory)
            {
                player.AddSeasonStat(stat);
            }

            result.Add(stat);
        }

        return result;
    }
}
