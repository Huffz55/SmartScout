using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.Analytics;

public class GetPlayerProfileQueryHandler : IRequestHandler<GetPlayerProfileQuery, PlayerScoutingReportDto?>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IPlayerAnalyticsService _analyticsService;

    public GetPlayerProfileQueryHandler(
        ISmartScoutDbContext dbContext,
        IPlayerAnalyticsService analyticsService)
    {
        _dbContext = dbContext;
        _analyticsService = analyticsService;
    }

    public async Task<PlayerScoutingReportDto?> Handle(GetPlayerProfileQuery request, CancellationToken cancellationToken)
    {
        var player = await _dbContext.Players
            .Include(p => p.SeasonStats)
            .FirstOrDefaultAsync(p => p.Id == request.PlayerId, cancellationToken);

        if (player == null)
            return null;

        var stat = (request.Season.HasValue
            ? player.SeasonStats.FirstOrDefault(s => s.Season == request.Season.Value)
            : null)
            ?? player.SeasonStats.OrderByDescending(s => s.Season).FirstOrDefault();

        if (stat == null)
        {
            return new PlayerScoutingReportDto
            {
                PlayerId = player.Id,
                FirstName = player.FirstName,
                LastName = player.LastName,
                Position = player.Position,
                Height = player.Height,
                WeightPounds = player.WeightPounds,
                TeamName = player.TeamName,
                League = player.League,
                Nationality = player.Nationality,
                Season = request.Season ?? DateTime.UtcNow.Year,
                RadarMetrics = new RadarMetricsDto(50, 50, 50, 50, 50, 50)
            };
        }

        var archetype = _analyticsService.DetermineArchetype(stat, player.Position, player.Height);
        var radar = _analyticsService.CalculateRadarMetrics(stat);
        double eci = _analyticsService.CalculateEuropeanCompatibilityIndex(stat);
        double dpi = _analyticsService.CalculateDefensivePressureIndex(stat);
        double buyLow = _analyticsService.CalculateBuyLowBounceBackIndex(stat, player.SeasonStats);

        return new PlayerScoutingReportDto
        {
            PlayerId = player.Id,
            FirstName = player.FirstName,
            LastName = player.LastName,
            Position = player.Position,
            Height = player.Height,
            WeightPounds = player.WeightPounds,
            TeamName = !string.IsNullOrWhiteSpace(stat.TeamName) ? stat.TeamName : player.TeamName,
            League = !string.IsNullOrWhiteSpace(stat.League) ? stat.League : player.League,
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
            PtsPer36 = stat.PtsPer36,
            AssistToTurnoverRatio = stat.Turnovers > 0 ? (stat.Assists / stat.Turnovers) : (stat.Assists ?? 0),
            FoulsDrawn = stat.FoulsDrawn,
            PointsPer40 = stat.PointsPer40,
            AssistsPer40 = stat.AssistsPer40,
            ReboundsPer40 = stat.ReboundsPer40,
            EuropeanCompatibilityIndex = eci,
            DefensivePressureIndex = dpi,
            BuyLowBounceBackIndex = buyLow
        };
    }
}
