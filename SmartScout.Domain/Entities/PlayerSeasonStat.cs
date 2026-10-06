using System;

namespace SmartScout.Domain.Entities;

public class PlayerSeasonStat
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public Player? Player { get; private set; } // Navigation property

    public int Season { get; private set; }
    public int GamesPlayed { get; private set; }
    public string? Minutes { get; private set; }
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

    // EF Core constructor
    private PlayerSeasonStat() { }

    public PlayerSeasonStat(Guid playerId, int season, int gamesPlayed, string? minutes, decimal? points, decimal? assists, decimal? rebounds, decimal? steals, decimal? blocks, decimal? fgPct, decimal? fg3Pct, decimal? ftPct, decimal? turnovers, decimal? trueShootingPct, decimal? playerEfficiencyRating, decimal? usagePct, decimal? totalReboundPct, decimal? assistPct, decimal? winShares, decimal? boxPlusMinus)
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
    }

    public void UpdateStats(int gamesPlayed, string? minutes, decimal? points, decimal? assists, decimal? rebounds, decimal? steals, decimal? blocks, decimal? fgPct, decimal? fg3Pct, decimal? ftPct, decimal? turnovers, decimal? trueShootingPct, decimal? playerEfficiencyRating, decimal? usagePct, decimal? totalReboundPct, decimal? assistPct, decimal? winShares, decimal? boxPlusMinus)
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
    }
}
