using System.Collections.Generic;

namespace SmartScout.Infrastructure.Services.EuroLeague;

public class EuroBoxscoreResponse
{
    public bool Live { get; set; }
    public List<EuroTeamStats>? Stats { get; set; }
}

public class EuroTeamStats
{
    public string? Team { get; set; }
    public string? Coach { get; set; }
    public List<EuroPlayerGameStats>? PlayersStats { get; set; }
}

public class EuroPlayerGameStats
{
    public string? Player_ID { get; set; }
    public int? IsStarter { get; set; }
    public int? IsPlaying { get; set; }
    public string? Team { get; set; }
    public string? Dorsal { get; set; }
    public string? Player { get; set; }
    public string? Minutes { get; set; }
    public int? Points { get; set; }
    public int? FieldGoalsMade2 { get; set; }
    public int? FieldGoalsAttempted2 { get; set; }
    public int? FieldGoalsMade3 { get; set; }
    public int? FieldGoalsAttempted3 { get; set; }
    public int? FreeThrowsMade { get; set; }
    public int? FreeThrowsAttempted { get; set; }
    public int? OffensiveRebounds { get; set; }
    public int? DefensiveRebounds { get; set; }
    public int? TotalRebounds { get; set; }
    public int? Assistances { get; set; }
    public int? Steals { get; set; }
    public int? Turnovers { get; set; }
    public int? BlocksFavour { get; set; }
    public int? BlocksAgainst { get; set; }
    public int? FoulsCommited { get; set; }
    public int? FoulsReceived { get; set; }
    public int? Valuation { get; set; }
    public int? Plusminus { get; set; }
}

public class EuroHeaderResponse
{
    public bool Live { get; set; }
    public string? Round { get; set; }
    public string? Date { get; set; }
    public string? TeamA { get; set; }
    public string? TeamB { get; set; }
    public string? CodeTeamA { get; set; }
    public string? CodeTeamB { get; set; }
    public string? ScoreA { get; set; }
    public string? ScoreB { get; set; }
    public string? Competition { get; set; }
}
