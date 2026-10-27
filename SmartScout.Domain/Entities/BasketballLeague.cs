using System;
using System.Collections.Generic;

namespace SmartScout.Domain.Entities;

public class BasketballLeague
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty; // e.g., "NBA", "EuroLeague", "EuroCup", "ACB", "BSL"
    public string Name { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string Region { get; private set; } = string.Empty; // "North America", "Europe", "International"
    public int TierLevel { get; private set; } // 1 to 5
    public decimal DifficultyCoefficient { get; private set; } // e.g., NBA = 1.00, EuroLeague = 0.88, ACB = 0.82, EuroCup = 0.78, BSL = 0.76
    public string? ExternalIdsJson { get; private set; } // JSONB mapping for external provider IDs

    private BasketballLeague() { }

    public BasketballLeague(
        string code,
        string name,
        string country,
        string region,
        int tierLevel,
        decimal difficultyCoefficient,
        string? externalIdsJson = null)
    {
        Id = Guid.NewGuid();
        Code = code;
        Name = name;
        Country = country;
        Region = region;
        TierLevel = tierLevel;
        DifficultyCoefficient = difficultyCoefficient;
        ExternalIdsJson = externalIdsJson;
    }

    public void UpdateMetadata(
        string name,
        string country,
        string region,
        int tierLevel,
        decimal difficultyCoefficient,
        string? externalIdsJson = null)
    {
        Name = name;
        Country = country;
        Region = region;
        TierLevel = tierLevel;
        DifficultyCoefficient = difficultyCoefficient;
        ExternalIdsJson = externalIdsJson ?? ExternalIdsJson;
    }

    public static readonly IReadOnlyDictionary<string, decimal> DefaultDifficultyCoefficients =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["NBA"] = 1.00m,
            ["EuroLeague"] = 0.88m,
            ["ACB"] = 0.82m,
            ["Spain ACB"] = 0.82m,
            ["EuroCup"] = 0.78m,
            ["BSL"] = 0.76m,
            ["Turkey BSL"] = 0.76m,
            ["BCL"] = 0.75m,
            ["VTB"] = 0.75m,
            ["ABA"] = 0.72m,
            ["BBL"] = 0.72m,
            ["Germany BBL"] = 0.72m,
            ["LNB"] = 0.72m,
            ["France LNB"] = 0.72m,
            ["LegaA"] = 0.72m,
            ["Italy Lega A"] = 0.72m,
            ["GBL"] = 0.71m,
            ["Greece GBL"] = 0.71m,
            ["G-League"] = 0.70m,
            ["GLeague"] = 0.70m,
            ["LKL"] = 0.68m,
            ["Lithuania LKL"] = 0.68m,
            ["NCAA"] = 0.65m,
            ["NCAAD1"] = 0.65m
        };

    public static decimal GetDifficultyCoefficient(string? leagueCode)
    {
        if (string.IsNullOrWhiteSpace(leagueCode)) return 0.75m;
        return DefaultDifficultyCoefficients.TryGetValue(leagueCode.Trim(), out decimal coeff) ? coeff : 0.72m;
    }

    public static IReadOnlyList<BasketballLeague> GetDefaultSeedLeagues() => new List<BasketballLeague>
    {
        new("NBA", "National Basketball Association", "USA", "North America", 1, 1.00m, "{\"balldontlie\":\"nba\",\"bref\":\"NBA\"}"),
        new("EuroLeague", "Turkish Airlines EuroLeague", "Pan-European", "Europe", 1, 0.88m, "{\"euroleagueApi\":\"E\"}"),
        new("ACB", "Liga Endesa ACB", "Spain", "Europe", 2, 0.82m, "{\"realgm\":\"acb\"}"),
        new("EuroCup", "BKT EuroCup", "Pan-European", "Europe", 2, 0.78m, "{\"euroleagueApi\":\"U\"}"),
        new("BSL", "Türkiye Sigorta Basketbol Süper Ligi", "Turkey", "Europe", 2, 0.76m, "{\"tbf\":\"bsl\"}"),
        new("BCL", "FIBA Basketball Champions League", "Pan-European", "Europe", 2, 0.75m, "{\"fiba\":\"bcl\"}"),
        new("VTB", "VTB United League", "International", "Europe", 2, 0.75m, "{\"vtb\":\"vtb\"}"),
        new("ABA", "AdmiralBet ABA League", "Adriatic", "Europe", 3, 0.72m, "{\"aba\":\"aba1\"}"),
        new("BBL", "easyCredit Basketball Bundesliga", "Germany", "Europe", 3, 0.72m, "{\"bbl\":\"de\"}"),
        new("LNB", "Betclic Élite LNB Pro A", "France", "Europe", 3, 0.72m, "{\"lnb\":\"proa\"}"),
        new("LegaA", "Lega Basket Serie A (LBA)", "Italy", "Europe", 3, 0.72m, "{\"lba\":\"seriea\"}"),
        new("GBL", "Stoiximan Greek Basket League", "Greece", "Europe", 3, 0.71m, "{\"esake\":\"gbl\"}"),
        new("GLeague", "NBA G-League", "USA", "North America", 3, 0.70m, "{\"bref\":\"gleague\"}"),
        new("LKL", "Betsafe-LKL Lithuania", "Lithuania", "Europe", 3, 0.68m, "{\"lkl\":\"lt\"}"),
        new("NCAAD1", "NCAA Division I Men's Basketball", "USA", "North America", 4, 0.65m, "{\"sportsref\":\"ncaa\"}")
    };
}
