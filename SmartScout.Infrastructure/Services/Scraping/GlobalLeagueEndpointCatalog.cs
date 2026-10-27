using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartScout.Infrastructure.Services.Scraping;

public record LeagueEndpointDefinition(
    string LeagueCode,
    string LeagueName,
    string PrimaryProvider,
    string PrimaryUrlTemplate,
    string? FallbackUrlTemplate = null,
    string? AdvancedUrlTemplate = null)
{
    public string BuildPrimaryUrl(int seasonEndYear) =>
        PrimaryUrlTemplate.Replace("{year}", seasonEndYear.ToString());

    public string? BuildFallbackUrl(int seasonEndYear) =>
        FallbackUrlTemplate?.Replace("{year}", seasonEndYear.ToString());

    public string? BuildAdvancedUrl(int seasonEndYear) =>
        AdvancedUrlTemplate?.Replace("{year}", seasonEndYear.ToString());
}

public static class GlobalLeagueEndpointCatalog
{
    private static readonly IReadOnlyList<LeagueEndpointDefinition> Definitions = new List<LeagueEndpointDefinition>
    {
        new(
            LeagueCode: "NBA",
            LeagueName: "National Basketball Association",
            PrimaryProvider: "BasketballReferenceLeagueTable",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/leagues/NBA_{year}_per_game.html",
            FallbackUrlTemplate: "https://www.basketball-reference.com/players/{letter}/{slug}.html",
            AdvancedUrlTemplate: "https://www.basketball-reference.com/leagues/NBA_{year}_advanced.html"),

        new(
            LeagueCode: "EuroLeague",
            LeagueName: "Turkish Airlines EuroLeague",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/euroleague/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/1/Euroleague/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "ACB",
            LeagueName: "Liga ACB (Spain)",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/spain-liga-acb/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/4/Spanish-ACB/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "EuroCup",
            LeagueName: "BKT EuroCup",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/eurocup/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/24/Eurocup/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "BSL",
            LeagueName: "Basketbol Süper Ligi (Turkey)",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/turkey-super-league/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/7/Turkish-BSL/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "BCL",
            LeagueName: "Basketball Champions League",
            PrimaryProvider: "RealGMInternational",
            PrimaryUrlTemplate: "https://basketball.realgm.com/international/league/106/Basketball-Champions-League-Europe/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/106/Basketball-Champions-League-Europe/stats/{year}/Averages/All/All/Season/All/points/desc/1/All"),

        new(
            LeagueCode: "VTB",
            LeagueName: "VTB United League",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/vtb-united/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/35/VTB-United-League/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "ABA",
            LeagueName: "ABA League First Division",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/aba-adriatic/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/18/Adriatic-League-Liga-ABA/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "BBL",
            LeagueName: "Basketball Bundesliga (Germany)",
            PrimaryProvider: "RealGMInternational",
            PrimaryUrlTemplate: "https://basketball.realgm.com/international/league/15/German-BBL/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/15/German-BBL/stats/{year}/Averages/All/All/Season/All/points/desc/1/All"),

        new(
            LeagueCode: "LNB",
            LeagueName: "LNB Pro A / Betclic Élite (France)",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/france-lnb-pro-a/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/12/French-Jeep-Elite/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "LegaA",
            LeagueName: "Lega Basket Serie A (Italy)",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/italy-basket-serie-a/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/6/Italian-Lega-Basket-Serie-A/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "GBL",
            LeagueName: "Greek Basket League (HEBA A1)",
            PrimaryProvider: "BasketballReferenceInternational",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/international/greek-basket-league/{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/8/Greek-HEBA-A1/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "GLeague",
            LeagueName: "NBA G-League",
            PrimaryProvider: "BasketballReferenceGLeague",
            PrimaryUrlTemplate: "https://www.basketball-reference.com/gleague/years/gleague_{year}_per_game.html",
            FallbackUrlTemplate: "https://basketball.realgm.com/dleague/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season"),

        new(
            LeagueCode: "LKL",
            LeagueName: "Lietuvos Krepšinio Lyga (Lithuania)",
            PrimaryProvider: "RealGMInternational",
            PrimaryUrlTemplate: "https://basketball.realgm.com/international/league/10/Lithuanian-LKL/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season",
            FallbackUrlTemplate: "https://basketball.realgm.com/international/league/10/Lithuanian-LKL/stats/{year}/Averages/All/All/Season/All/points/desc/1/All"),

        new(
            LeagueCode: "NCAAD1",
            LeagueName: "NCAA Division I",
            PrimaryProvider: "RealGMNcaa",
            PrimaryUrlTemplate: "https://basketball.realgm.com/ncaa/stats/{year}/Averages/Qualified/All/Season/All/points/desc/1/Regular_Season",
            FallbackUrlTemplate: "https://www.sports-reference.com/cbb/seasons/men/{year}-leaders.html")
    };

    public static IReadOnlyList<LeagueEndpointDefinition> GetAll() => Definitions;

    public static LeagueEndpointDefinition? GetByCode(string leagueCode) =>
        Definitions.FirstOrDefault(d => string.Equals(d.LeagueCode, leagueCode?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Default canonical order of international/domestic leagues for full-batch ingestion.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultAllLeagueCodes = new[]
    {
        "NBA", "EuroLeague", "EuroCup", "ACB", "BSL", "BCL", "ABA", 
        "BBL", "LNB", "LegaA", "GBL", "LKL", "GLeague", "VTB"
    };

    /// <summary>
    /// Returns domestic and international leagues scraped via fast HTML season tables (including NBA, EuroLeague and EuroCup).
    /// </summary>
    public static IReadOnlyList<LeagueEndpointDefinition> GetTableScrapedLeagues()
    {
        var orderMap = DefaultAllLeagueCodes
            .Select((code, idx) => (code, idx))
            .ToDictionary(x => x.code, x => x.idx, StringComparer.OrdinalIgnoreCase);

        return Definitions
            .OrderBy(d => orderMap.TryGetValue(d.LeagueCode, out int idx) ? idx : 999)
            .ToList();
    }
}
