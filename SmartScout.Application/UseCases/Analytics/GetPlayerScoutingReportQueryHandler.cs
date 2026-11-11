using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.Analytics;

public class GetPlayerScoutingReportQueryHandler : IRequestHandler<GetPlayerScoutingReportQuery, byte[]>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IPlayerAnalyticsService _analyticsService;
    private readonly IScoutingReportGenerator _reportGenerator;

    public GetPlayerScoutingReportQueryHandler(
        ISmartScoutDbContext dbContext,
        IPlayerAnalyticsService analyticsService,
        IScoutingReportGenerator reportGenerator)
    {
        _dbContext = dbContext;
        _analyticsService = analyticsService;
        _reportGenerator = reportGenerator;
    }

    public async Task<byte[]> Handle(GetPlayerScoutingReportQuery request, CancellationToken cancellationToken)
    {
        var stat = await _dbContext.SeasonStats
            .Include(s => s.Player)
                .ThenInclude(p => p!.SeasonStats)
            .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerId && s.Season == request.Season, cancellationToken)
            ?? await _dbContext.SeasonStats
                .Include(s => s.Player)
                    .ThenInclude(p => p!.SeasonStats)
                .OrderByDescending(s => s.Season)
                .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerId, cancellationToken);

        if (stat == null || stat.Player == null)
        {
            throw new KeyNotFoundException($"Season {request.Season} stats for player '{request.PlayerId}' were not found.");
        }

        var player = stat.Player;
        var archetype = _analyticsService.DetermineArchetype(stat, player.Position, player.Height);
        var radar = _analyticsService.CalculateRadarMetrics(stat);
        double eci = _analyticsService.CalculateEuropeanCompatibilityIndex(stat);
        double dpi = _analyticsService.CalculateDefensivePressureIndex(stat);
        double buyLow = _analyticsService.CalculateBuyLowBounceBackIndex(stat, player.SeasonStats);

        string verdict = GenerateTacticalVerdict(player.FirstName, player.LastName, archetype.ToString(), stat, radar, eci, dpi, buyLow, player.CurrentAge, player.Position);

        var reportDto = new PlayerScoutingReportDto
        {
            PlayerId = player.Id,
            FirstName = player.FirstName,
            LastName = player.LastName,
            Position = player.Position,
            Height = player.Height,
            HeightCm = player.HeightCm,
            WeightPounds = player.WeightPounds,
            WeightKg = player.WeightKg,
            WingspanCm = player.WingspanCm,
            StandingReachCm = player.StandingReachCm,
            Age = player.CurrentAge,
            BirthDate = player.BirthDate,
            ContractExpirySeason = player.ContractExpirySeason,
            EstimatedMarketValueTier = player.EstimatedMarketValueTier,
            AgencyName = player.AgencyName,
            DominantHand = player.DominantHand,
            ScoutNotes = player.ScoutNotes,
            IsWatchlisted = player.IsWatchlisted,
            TeamName = !string.IsNullOrWhiteSpace(stat.TeamName) ? stat.TeamName : player.TeamName,
            League = !string.IsNullOrWhiteSpace(stat.League) ? stat.League : player.League,
            Season = stat.Season,
            Archetype = archetype.ToString(),
            RadarMetrics = radar,
            EuroLeagueTransitionIndex = stat.EuroLeagueTransitionIndex,
            StocksPer36 = stat.StocksPer36 ?? ((stat.StlPer36 ?? 0) + (stat.BlkPer36 ?? 0)),
            ThreePointRate = stat.ThreePointRate,
            Percentiles = _analyticsService.CalculatePositionalPercentiles(stat, player.Position),
            CareerHistory = player.SeasonStats
                .OrderBy(s => s.Season)
                .Select(s => new CareerSeasonStintDto
                {
                    Season = s.Season,
                    League = !string.IsNullOrWhiteSpace(s.League) ? s.League : player.League,
                    TeamName = !string.IsNullOrWhiteSpace(s.TeamName) ? s.TeamName : player.TeamName,
                    GamesPlayed = s.GamesPlayed,
                    Minutes = s.Minutes,
                    Points = s.Points,
                    Rebounds = s.Rebounds,
                    Assists = s.Assists,
                    Fg3Pct = s.Fg3Pct,
                    TrueShootingPct = s.TrueShootingPct,
                    PtsPer36 = s.PtsPer36 ?? (s.Minutes > 0 && s.Points.HasValue ? Math.Round((s.Points.Value / s.Minutes.Value) * 36m, 1) : null),
                    StocksPer36 = s.StocksPer36 ?? ((s.StlPer36 ?? 0) + (s.BlkPer36 ?? 0)),
                    Pir = s.Pir,
                    EuroLeagueTransitionIndex = s.EuroLeagueTransitionIndex,
                    LeagueDifficultyCoefficient = s.LeagueDifficultyCoefficient
                })
                .ToList(),
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
            TotalReboundPct = stat.TotalReboundPct,
            AssistPct = stat.AssistPct,
            WinShares = stat.WinShares,
            BoxPlusMinus = stat.BoxPlusMinus,
            Nationality = player.Nationality,
            Pir = stat.Pir,
            PtsPer36 = stat.PtsPer36,
            AssistToTurnoverRatio = stat.Turnovers > 0 ? (stat.Assists / stat.Turnovers) : (stat.Assists ?? 0),
            FoulsDrawn = stat.FoulsDrawn,
            PointsPer40 = stat.PointsPer40,
            AssistsPer40 = stat.AssistsPer40,
            ReboundsPer40 = stat.ReboundsPer40,
            EuropeanCompatibilityIndex = eci,
            DefensivePressureIndex = dpi,
            BuyLowBounceBackIndex = buyLow,
            TacticalVerdict = verdict
        };

        _analyticsService.PopulateShotDietAndPhysicality(reportDto, stat);

        return _reportGenerator.GenerateReportPdf(reportDto);
    }

    private static string GenerateTacticalVerdict(
        string firstName, 
        string lastName, 
        string archetype, 
        Domain.Entities.PlayerSeasonStat stat, 
        RadarMetricsDto radar,
        double eci,
        double dpi,
        double buyLow,
        int? age,
        string? position)
    {
        string fullName = $"{firstName} {lastName}".Trim();
        string tier = radar.OverallImpact switch
        {
            >= 85 => "MVP-Caliber Franchise Cornerstone",
            >= 70 => "All-Star Caliber Primary Contributor",
            >= 55 => "High-Impact Starter / Key Rotation Piece",
            >= 40 => "Solid Role Player / Situational Specialist",
            _ => (age.HasValue && age.Value >= 28)
                ? (position != null && (position.Contains("C") || position.Contains("F"))
                    ? "Rotational Frontcourt Specialist / Veteran Spark"
                    : "Rotational Perimeter Specialist / Veteran Spark")
                : (age.HasValue && age.Value <= 23)
                    ? "Developmental Prospect / High-Upside Asset"
                    : "System Depth Asset / Situational Contributor"
        };

        string shootingNote = (stat.TrueShootingPct ?? 0) >= 0.58m 
            ? "exhibits elite shooting efficiency above league median" 
            : "operates with standard offensive efficiency";

        string euroInsight = string.Empty;
        if (dpi >= 70.0)
        {
            euroInsight += $" Exceptional defensive perimeter ball-pressure (DPI: {dpi.ToString("F1", CultureInfo.InvariantCulture)}/100) makes the player an ideal fit for Dušan Alimpijević / Beşiktaş high-pressure trapping systems.";
        }
        if (eci >= 70.0)
        {
            euroInsight += $" Outstanding European Compatibility Index (ECI: {eci.ToString("F1", CultureInfo.InvariantCulture)}/100) indicates high tactical acumen and floor-spacing reliability for European half-court structures.";
        }
        if (buyLow >= 70.0)
        {
            euroInsight += $" High Moneyball Buy-Low / Bounce-Back Index ({buyLow.ToString("F1", CultureInfo.InvariantCulture)}/100) flags a premier undervalued target with proven ceiling and Per-40 breakout upside.";
        }

        return $"{fullName} is classified as a {archetype} ({tier}). In this campaign, the player {shootingNote} " +
               $"with an overall impact rating of {radar.OverallImpact.ToString("F1", CultureInfo.InvariantCulture)}/100 and scoring score of {radar.Scoring.ToString("F1", CultureInfo.InvariantCulture)}/100." +
               $"{euroInsight} Recommended for strategic tactical deployments fitting a high-leverage {archetype} role.";
    }
}
