using System;
using SmartScout.Domain.Services;

namespace SmartScout.Domain.Entities;

public class PlayerSeasonStat
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public Player? Player { get; private set; } // Navigation property

    // Stint Composite Key Identifiers: PlayerId + Season + League + TeamName + CompetitionPhase
    public int Season { get; private set; }
    public string League { get; private set; } = "NBA";
    public string TeamName { get; private set; } = "TOT";
    public string CompetitionPhase { get; private set; } = "Regular Season";
    public decimal LeagueDifficultyCoefficient { get; private set; } = 1.00m;

    // 1. Participation
    public int GamesPlayed { get; private set; }
    public int? GamesStarted { get; private set; }
    public decimal? MinutesPlayedTotal { get; private set; }
    public decimal? Minutes { get; private set; } // MinutesPerGame (MPG)

    // 2. Traditional Box Score (Per Game)
    public decimal? Points { get; private set; }
    public decimal? FieldGoalsMade { get; private set; }
    public decimal? FieldGoalsAttempted { get; private set; }
    public decimal? FgPct { get; private set; }
    public decimal? ThreePointsMade { get; private set; }
    public decimal? ThreePointsAttempted { get; private set; }
    public decimal? Fg3Pct { get; private set; }
    public decimal? TwoPointsMade { get; private set; }
    public decimal? TwoPointsAttempted { get; private set; }
    public decimal? Fg2Pct { get; private set; }
    public decimal? FreeThrowsMade { get; private set; }
    public decimal? FreeThrowsAttempted { get; private set; }
    public decimal? FtPct { get; private set; }
    public decimal? OffensiveRebounds { get; private set; }
    public decimal? DefensiveRebounds { get; private set; }
    public decimal? Rebounds { get; private set; } // TotalRebounds (TRB)
    public decimal? Assists { get; private set; }
    public decimal? Steals { get; private set; }
    public decimal? Blocks { get; private set; }
    public decimal? Turnovers { get; private set; }
    public decimal? PersonalFoulsCommitted { get; private set; }
    public decimal? FoulsDrawn { get; private set; } // PersonalFoulsDrawn (PFD)
    public decimal? PlusMinus { get; private set; }

    // 3. Per-36 & Per-40 Normalized Stats
    public decimal? PtsPer36 { get; private set; }
    public decimal? RebPer36 { get; private set; }
    public decimal? AstPer36 { get; private set; }
    public decimal? StlPer36 { get; private set; }
    public decimal? BlkPer36 { get; private set; }
    public decimal? TovPer36 { get; private set; }
    public decimal? PointsPer40 { get; private set; }
    public decimal? AssistsPer40 { get; private set; }
    public decimal? ReboundsPer40 { get; private set; }

    // 4. Advanced Metrics (Auto-calculated via StatCalculationService if source omits them)
    public decimal? TrueShootingPct { get; private set; }
    public decimal? EffectiveFgPct { get; private set; }
    public decimal? UsagePct { get; private set; }
    public decimal? AssistToTurnoverRatio { get; private set; }
    public decimal? GameScoreAverage { get; private set; }
    public decimal? Pir { get; private set; } // Performance Index Rating
    public decimal? PlayerEfficiencyRating { get; private set; }
    public decimal? TotalReboundPct { get; private set; }
    public decimal? AssistPct { get; private set; }
    public decimal? WinShares { get; private set; }
    public decimal? BoxPlusMinus { get; private set; }

    // 5. Future-Proof Raw JSONB Backup
    public string? RawScrapedPayload { get; private set; }

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
        decimal? reboundsPer40 = null,
        string? league = null,
        string? teamName = null,
        string? competitionPhase = null,
        int? gamesStarted = null,
        decimal? minutesPlayedTotal = null,
        decimal? fgm = null,
        decimal? fga = null,
        decimal? fg3m = null,
        decimal? fg3a = null,
        decimal? fg2m = null,
        decimal? fg2a = null,
        decimal? fg2Pct = null,
        decimal? ftm = null,
        decimal? fta = null,
        decimal? orb = null,
        decimal? drb = null,
        decimal? pfCommitted = null,
        decimal? plusMinus = null,
        decimal? effectiveFgPct = null,
        decimal? assistToTurnoverRatio = null,
        decimal? gameScoreAverage = null,
        string? rawScrapedPayload = null)
    {
        Id = Guid.NewGuid();
        PlayerId = playerId;
        Season = season;
        League = string.IsNullOrWhiteSpace(league) ? "NBA" : league.Trim();
        TeamName = string.IsNullOrWhiteSpace(teamName) ? "TOT" : teamName.Trim();
        CompetitionPhase = string.IsNullOrWhiteSpace(competitionPhase) ? "Regular Season" : competitionPhase.Trim();
        LeagueDifficultyCoefficient = BasketballLeague.GetDifficultyCoefficient(League);

        GamesPlayed = gamesPlayed;
        GamesStarted = gamesStarted;
        Minutes = minutes;
        MinutesPlayedTotal = minutesPlayedTotal ?? (minutes.HasValue && gamesPlayed > 0 ? Math.Round(minutes.Value * gamesPlayed, 1) : null);

        Points = points;
        FieldGoalsMade = fgm;
        FieldGoalsAttempted = fga;
        FgPct = fgPct;
        ThreePointsMade = fg3m;
        ThreePointsAttempted = fg3a;
        Fg3Pct = fg3Pct;
        TwoPointsMade = fg2m;
        TwoPointsAttempted = fg2a;
        Fg2Pct = fg2Pct;
        FreeThrowsMade = ftm;
        FreeThrowsAttempted = fta;
        FtPct = ftPct;
        OffensiveRebounds = orb;
        DefensiveRebounds = drb;
        Rebounds = rebounds;
        Assists = assists;
        Steals = steals;
        Blocks = blocks;
        Turnovers = turnovers;
        PersonalFoulsCommitted = pfCommitted;
        FoulsDrawn = foulsDrawn;
        PlusMinus = plusMinus;

        TrueShootingPct = trueShootingPct;
        EffectiveFgPct = effectiveFgPct;
        PlayerEfficiencyRating = playerEfficiencyRating;
        UsagePct = usagePct;
        AssistToTurnoverRatio = assistToTurnoverRatio;
        GameScoreAverage = gameScoreAverage;
        TotalReboundPct = totalReboundPct;
        AssistPct = assistPct;
        WinShares = winShares;
        BoxPlusMinus = boxPlusMinus;
        Pir = pir;
        PointsPer40 = pointsPer40;
        AssistsPer40 = assistsPer40;
        ReboundsPer40 = reboundsPer40;
        RawScrapedPayload = rawScrapedPayload;

        StatCalculationService.EnrichAndCalculate(this);
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
        decimal? reboundsPer40 = null,
        string? league = null,
        string? teamName = null,
        string? competitionPhase = null,
        int? gamesStarted = null,
        decimal? minutesPlayedTotal = null,
        decimal? fgm = null,
        decimal? fga = null,
        decimal? fg3m = null,
        decimal? fg3a = null,
        decimal? fg2m = null,
        decimal? fg2a = null,
        decimal? fg2Pct = null,
        decimal? ftm = null,
        decimal? fta = null,
        decimal? orb = null,
        decimal? drb = null,
        decimal? pfCommitted = null,
        decimal? plusMinus = null,
        decimal? effectiveFgPct = null,
        decimal? assistToTurnoverRatio = null,
        decimal? gameScoreAverage = null,
        string? rawScrapedPayload = null)
    {
        GamesPlayed = gamesPlayed;
        GamesStarted = gamesStarted ?? GamesStarted;
        Minutes = minutes;
        MinutesPlayedTotal = minutesPlayedTotal ?? (minutes.HasValue && gamesPlayed > 0 ? Math.Round(minutes.Value * gamesPlayed, 1) : MinutesPlayedTotal);

        Points = points;
        FieldGoalsMade = fgm ?? FieldGoalsMade;
        FieldGoalsAttempted = fga ?? FieldGoalsAttempted;
        FgPct = fgPct;
        ThreePointsMade = fg3m ?? ThreePointsMade;
        ThreePointsAttempted = fg3a ?? ThreePointsAttempted;
        Fg3Pct = fg3Pct;
        TwoPointsMade = fg2m ?? TwoPointsMade;
        TwoPointsAttempted = fg2a ?? TwoPointsAttempted;
        Fg2Pct = fg2Pct ?? Fg2Pct;
        FreeThrowsMade = ftm ?? FreeThrowsMade;
        FreeThrowsAttempted = fta ?? FreeThrowsAttempted;
        FtPct = ftPct;
        OffensiveRebounds = orb ?? OffensiveRebounds;
        DefensiveRebounds = drb ?? DefensiveRebounds;
        Rebounds = rebounds;
        Assists = assists;
        Steals = steals;
        Blocks = blocks;
        Turnovers = turnovers;
        PersonalFoulsCommitted = pfCommitted ?? PersonalFoulsCommitted;
        FoulsDrawn = foulsDrawn ?? FoulsDrawn;
        PlusMinus = plusMinus ?? PlusMinus;

        TrueShootingPct = trueShootingPct;
        EffectiveFgPct = effectiveFgPct;
        PlayerEfficiencyRating = playerEfficiencyRating ?? PlayerEfficiencyRating;
        UsagePct = usagePct;
        AssistToTurnoverRatio = assistToTurnoverRatio;
        GameScoreAverage = gameScoreAverage;
        TotalReboundPct = totalReboundPct ?? TotalReboundPct;
        AssistPct = assistPct ?? AssistPct;
        WinShares = winShares ?? WinShares;
        BoxPlusMinus = boxPlusMinus ?? BoxPlusMinus;
        Pir = pir;
        PointsPer40 = pointsPer40;
        AssistsPer40 = assistsPer40;
        ReboundsPer40 = reboundsPer40;

        if (!string.IsNullOrWhiteSpace(league))
        {
            League = league.Trim();
            LeagueDifficultyCoefficient = BasketballLeague.GetDifficultyCoefficient(League);
        }
        if (!string.IsNullOrWhiteSpace(teamName))
        {
            TeamName = teamName.Trim();
        }
        if (!string.IsNullOrWhiteSpace(competitionPhase))
        {
            CompetitionPhase = competitionPhase.Trim();
        }
        if (!string.IsNullOrWhiteSpace(rawScrapedPayload))
        {
            RawScrapedPayload = rawScrapedPayload;
        }

        StatCalculationService.EnrichAndCalculate(this);
    }

    internal void SetCalculatedMetrics(
        decimal leagueDifficultyCoefficient,
        decimal? ptsPer36,
        decimal? rebPer36,
        decimal? astPer36,
        decimal? stlPer36,
        decimal? blkPer36,
        decimal? tovPer36,
        decimal? ptsPer40,
        decimal? rebPer40,
        decimal? astPer40,
        decimal? tsPct,
        decimal? eFgPct,
        decimal? usgPct,
        decimal? astTovRatio,
        decimal? gameScoreAvg,
        decimal? pirVal)
    {
        LeagueDifficultyCoefficient = leagueDifficultyCoefficient;
        PtsPer36 = ptsPer36;
        RebPer36 = rebPer36;
        AstPer36 = astPer36;
        StlPer36 = stlPer36;
        BlkPer36 = blkPer36;
        TovPer36 = tovPer36;
        PointsPer40 = ptsPer40;
        ReboundsPer40 = rebPer40;
        AssistsPer40 = astPer40;
        TrueShootingPct = tsPct;
        EffectiveFgPct = eFgPct;
        UsagePct = usgPct;
        AssistToTurnoverRatio = astTovRatio;
        GameScoreAverage = gameScoreAvg;
        Pir = pirVal;
    }
}
