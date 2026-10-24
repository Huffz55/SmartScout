using System;

namespace SmartScout.Domain.Entities;

public class PlayerSeasonStat
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public Player? Player { get; private set; } // Navigation property

    public int Season { get; private set; }
    public int GamesPlayed { get; private set; }
    public decimal? Minutes { get; private set; }
    public decimal? Points { get; private set; }
    public decimal? Assists { get; private set; }
    public decimal? Rebounds { get; private set; }
    public decimal? Steals { get; private set; }
    public decimal? Blocks { get; private set; }
    public decimal? FgPct { get; private set; }
    public decimal? Fg3Pct { get; private set; }
    public decimal? FtPct { get; private set; }
    public decimal? Turnovers { get; private set; }

    // Advanced Stats
    public decimal? TrueShootingPct { get; private set; }
    public decimal? PlayerEfficiencyRating { get; private set; }
    public decimal? UsagePct { get; private set; }
    public decimal? TotalReboundPct { get; private set; }
    public decimal? AssistPct { get; private set; }
    public decimal? WinShares { get; private set; }
    public decimal? BoxPlusMinus { get; private set; }

    // European & Per-40 Regulation Analytics
    public decimal? Pir { get; private set; } // Performance Index Rating (EuroLeague/EuroCup)
    public decimal? FoulsDrawn { get; private set; } // Crucial metric for physical European half-court play
    public decimal? PointsPer40 { get; private set; }
    public decimal? AssistsPer40 { get; private set; }
    public decimal? ReboundsPer40 { get; private set; }

    // EF Core constructor
    private PlayerSeasonStat() { }

    public PlayerSeasonStat(
        Guid playerId, 
        int season, 
        int gamesPlayed, 
        decimal? minutes, 
        decimal? points, 
        decimal? assists, 
        decimal? rebounds, 
        decimal? steals, 
        decimal? blocks, 
        decimal? fgPct, 
        decimal? fg3Pct, 
        decimal? ftPct, 
        decimal? turnovers, 
        decimal? trueShootingPct = null, 
        decimal? playerEfficiencyRating = null, 
        decimal? usagePct = null, 
        decimal? totalReboundPct = null, 
        decimal? assistPct = null, 
        decimal? winShares = null, 
        decimal? boxPlusMinus = null,
        decimal? pir = null,
        decimal? foulsDrawn = null,
        decimal? pointsPer40 = null,
        decimal? assistsPer40 = null,
        decimal? reboundsPer40 = null)
    {
        Id = Guid.NewGuid();
        PlayerId = playerId;
        Season = season;
        GamesPlayed = gamesPlayed;
        Minutes = minutes;
        Points = points;
        Assists = assists;
        Rebounds = rebounds;
        Steals = steals;
        Blocks = blocks;
        FgPct = fgPct;
        Fg3Pct = fg3Pct;
        FtPct = ftPct;
        Turnovers = turnovers;
        TrueShootingPct = trueShootingPct;
        PlayerEfficiencyRating = playerEfficiencyRating;
        UsagePct = usagePct;
        TotalReboundPct = totalReboundPct;
        AssistPct = assistPct;
        WinShares = winShares;
        BoxPlusMinus = boxPlusMinus;

        Pir = pir;
        FoulsDrawn = foulsDrawn;

        // Auto-calculate Per-40 regulation stats if not provided and minutes > 0
        if (minutes.HasValue && minutes.Value > 0)
        {
            PointsPer40 = pointsPer40 ?? (points.HasValue ? Math.Round((points.Value / minutes.Value) * 40m, 1) : null);
            AssistsPer40 = assistsPer40 ?? (assists.HasValue ? Math.Round((assists.Value / minutes.Value) * 40m, 1) : null);
            ReboundsPer40 = reboundsPer40 ?? (rebounds.HasValue ? Math.Round((rebounds.Value / minutes.Value) * 40m, 1) : null);
        }
        else
        {
            PointsPer40 = pointsPer40;
            AssistsPer40 = assistsPer40;
            ReboundsPer40 = reboundsPer40;
        }
    }

    public void UpdateStats(
        int gamesPlayed, 
        decimal? minutes, 
        decimal? points, 
        decimal? assists, 
        decimal? rebounds, 
        decimal? steals, 
        decimal? blocks, 
        decimal? fgPct, 
        decimal? fg3Pct, 
        decimal? ftPct, 
        decimal? turnovers, 
        decimal? trueShootingPct = null, 
        decimal? playerEfficiencyRating = null, 
        decimal? usagePct = null, 
        decimal? totalReboundPct = null, 
        decimal? assistPct = null, 
        decimal? winShares = null, 
        decimal? boxPlusMinus = null,
        decimal? pir = null,
        decimal? foulsDrawn = null,
        decimal? pointsPer40 = null,
        decimal? assistsPer40 = null,
        decimal? reboundsPer40 = null)
    {
        GamesPlayed = gamesPlayed;
        Minutes = minutes;
        Points = points;
        Assists = assists;
        Rebounds = rebounds;
        Steals = steals;
        Blocks = blocks;
        FgPct = fgPct;
        Fg3Pct = fg3Pct;
        FtPct = ftPct;
        Turnovers = turnovers;
        TrueShootingPct = trueShootingPct;
        PlayerEfficiencyRating = playerEfficiencyRating;
        UsagePct = usagePct;
        TotalReboundPct = totalReboundPct;
        AssistPct = assistPct;
        WinShares = winShares;
        BoxPlusMinus = boxPlusMinus;

        Pir = pir ?? Pir;
        FoulsDrawn = foulsDrawn ?? FoulsDrawn;

        if (minutes.HasValue && minutes.Value > 0)
        {
            PointsPer40 = pointsPer40 ?? (points.HasValue ? Math.Round((points.Value / minutes.Value) * 40m, 1) : PointsPer40);
            AssistsPer40 = assistsPer40 ?? (assists.HasValue ? Math.Round((assists.Value / minutes.Value) * 40m, 1) : AssistsPer40);
            ReboundsPer40 = reboundsPer40 ?? (rebounds.HasValue ? Math.Round((rebounds.Value / minutes.Value) * 40m, 1) : ReboundsPer40);
        }
        else
        {
            PointsPer40 = pointsPer40 ?? PointsPer40;
            AssistsPer40 = assistsPer40 ?? AssistsPer40;
            ReboundsPer40 = reboundsPer40 ?? ReboundsPer40;
        }
    }
}
