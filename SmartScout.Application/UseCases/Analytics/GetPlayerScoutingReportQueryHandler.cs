using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using System;
using System.Collections.Generic;
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
            .FirstOrDefaultAsync(s => s.PlayerId == request.PlayerId && s.Season == request.Season, cancellationToken);

        if (stat == null || stat.Player == null)
        {
            throw new KeyNotFoundException($"Season {request.Season} stats for player '{request.PlayerId}' were not found.");
        }

        var player = stat.Player;
        var archetype = _analyticsService.DetermineArchetype(stat, player.Position, player.Height);
        var radar = _analyticsService.CalculateRadarMetrics(stat);
        double eci = _analyticsService.CalculateEuropeanCompatibilityIndex(stat);
        double dpi = _analyticsService.CalculateDefensivePressureIndex(stat);

        string verdict = GenerateTacticalVerdict(player.FirstName, player.LastName, archetype.ToString(), stat, radar, eci, dpi);

        var reportDto = new PlayerScoutingReportDto
        {
            PlayerId = player.Id,
            FirstName = player.FirstName,
            LastName = player.LastName,
            Position = player.Position,
            Height = player.Height,
            WeightPounds = player.WeightPounds,
            TeamName = player.TeamName,
            League = player.League,
            Season = stat.Season,
            Archetype = archetype.ToString(),
            RadarMetrics = radar,
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
            FoulsDrawn = stat.FoulsDrawn,
            PointsPer40 = stat.PointsPer40,
            AssistsPer40 = stat.AssistsPer40,
            ReboundsPer40 = stat.ReboundsPer40,
            EuropeanCompatibilityIndex = eci,
            DefensivePressureIndex = dpi,
            TacticalVerdict = verdict
        };

        return _reportGenerator.GenerateReportPdf(reportDto);
    }

    private static string GenerateTacticalVerdict(
        string firstName, 
        string lastName, 
        string archetype, 
        Domain.Entities.PlayerSeasonStat stat, 
        RadarMetricsDto radar,
        double eci,
        double dpi)
    {
        string fullName = $"{firstName} {lastName}".Trim();
        string tier = radar.OverallImpact switch
        {
            >= 85 => "MVP-Caliber Franchise Cornerstone",
            >= 70 => "All-Star Caliber Primary Contributor",
            >= 55 => "High-Impact Starter / Key Rotation Piece",
            >= 40 => "Solid Role Player / Situational Specialist",
            _ => "Developmental / Depth Asset"
        };

        string shootingNote = (stat.TrueShootingPct ?? 0) >= 0.58m 
            ? "exhibits elite shooting efficiency above league median" 
            : "operates with standard offensive efficiency";

        string euroInsight = string.Empty;
        if (dpi >= 70.0)
        {
            euroInsight += $" Exceptional defensive perimeter ball-pressure (DPI: {dpi:F1}/100) makes the player an ideal fit for Dušan Alimpijević / Beşiktaş high-pressure trapping systems.";
        }
        if (eci >= 70.0)
        {
            euroInsight += $" Outstanding European Compatibility Index (ECI: {eci:F1}/100) indicates high tactical acumen and floor-spacing reliability for European half-court structures.";
        }

        return $"{fullName} is classified as a {archetype} ({tier}). In this campaign, the player {shootingNote} " +
               $"with an overall impact rating of {radar.OverallImpact:F1}/100 and scoring score of {radar.Scoring:F1}/100." +
               $"{euroInsight} Recommended for strategic tactical deployments fitting a high-leverage {archetype} role.";
    }
}
