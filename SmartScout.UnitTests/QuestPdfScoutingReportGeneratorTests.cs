using SmartScout.Application.DTOs;
using SmartScout.Infrastructure.Services.Reporting;
using System;
using System.Text;
using Xunit;

namespace SmartScout.UnitTests;

public class QuestPdfScoutingReportGeneratorTests
{
    private readonly QuestPdfScoutingReportGenerator _generator = new();

    [Fact]
    public void GenerateReportPdf_WithValidModel_ShouldReturnValidPdfBytes()
    {
        var model = new PlayerScoutingReportDto
        {
            PlayerId = Guid.NewGuid(),
            FirstName = "Luka",
            LastName = "Dončić",
            Position = "G-F",
            Height = "6-7",
            WeightPounds = 230,
            TeamName = "Dallas Mavericks",
            League = "NBA",
            Season = 2024,
            Archetype = "PointCenter",
            RadarMetrics = new RadarMetricsDto(92.5, 84.1, 95.0, 81.2, 55.4, 91.8),
            GamesPlayed = 70,
            Minutes = 37.5m,
            Points = 33.9m,
            Assists = 9.8m,
            Rebounds = 9.2m,
            Steals = 1.4m,
            Blocks = 0.5m,
            FgPct = 0.487m,
            Fg3Pct = 0.382m,
            FtPct = 0.786m,
            Turnovers = 4.0m,
            TrueShootingPct = 0.617m,
            PlayerEfficiencyRating = 28.1m,
            UsagePct = 36.0m,
            TotalReboundPct = 13.5m,
            AssistPct = 44.0m,
            WinShares = 12.0m,
            BoxPlusMinus = 9.9m,
            TacticalVerdict = "Elite offensive engine and primary playmaker. Operates with generational pick-and-roll vision and step-back shooting threat."
        };

        byte[] pdfBytes = _generator.GenerateReportPdf(model);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 1000, "PDF size should be substantial (> 1KB)");

        // Check PDF header signature '%PDF-'
        string header = Encoding.ASCII.GetString(pdfBytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void GenerateReportPdf_WithMissingOptionalStats_ShouldRenderWithoutThrowing()
    {
        var model = new PlayerScoutingReportDto
        {
            PlayerId = Guid.NewGuid(),
            FirstName = "Euro",
            LastName = "Prospect",
            Position = null,
            Height = null,
            WeightPounds = null,
            TeamName = null,
            League = "EuroLeague",
            Season = 2024,
            Archetype = "RolePlayer",
            RadarMetrics = new RadarMetricsDto(50.0, 50.0, 50.0, 50.0, 50.0, 50.0),
            GamesPlayed = 15,
            Minutes = null,
            Points = null,
            Assists = null,
            Rebounds = null,
            Steals = null,
            Blocks = null,
            FgPct = null,
            Fg3Pct = null,
            FtPct = null,
            Turnovers = null,
            TrueShootingPct = null,
            PlayerEfficiencyRating = null,
            UsagePct = null,
            TotalReboundPct = null,
            AssistPct = null,
            WinShares = null,
            BoxPlusMinus = null,
            TacticalVerdict = "Limited data sample."
        };

        byte[] pdfBytes = _generator.GenerateReportPdf(model);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 1000);
        string header = Encoding.ASCII.GetString(pdfBytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void GenerateReportPdf_WithEuropeanAndBesiktasMetrics_ShouldRenderComprehensivePdf()
    {
        var model = new PlayerScoutingReportDto
        {
            PlayerId = Guid.NewGuid(),
            FirstName = "Derek",
            LastName = "Needham",
            Position = "PG",
            Height = "6-0",
            WeightPounds = 185,
            TeamName = "Besiktas Fibabanka",
            League = "EuroCup",
            Nationality = "MNE",
            Season = 2024,
            Archetype = "FloorGeneral",
            RadarMetrics = new RadarMetricsDto(78.5, 82.0, 88.5, 45.0, 85.0, 81.0),
            GamesPlayed = 22,
            Minutes = 24.5m,
            Points = 13.8m,
            Assists = 4.8m,
            Rebounds = 2.4m,
            Steals = 1.7m,
            Blocks = 0.2m,
            FgPct = 0.450m,
            Fg3Pct = 0.405m,
            FtPct = 0.860m,
            Turnovers = 1.9m,
            TrueShootingPct = 0.610m,
            Pir = 15.8m,
            FoulsDrawn = 4.1m,
            PointsPer40 = 22.5m,
            AssistsPer40 = 7.8m,
            ReboundsPer40 = 3.9m,
            EuropeanCompatibilityIndex = 82.5,
            DefensivePressureIndex = 86.0,
            TacticalVerdict = "Derek Needham exhibits elite perimeter pressure and half-court poise. Outstanding Alimpijević System fit (DPI: 86.0/100) and European Compatibility Index (ECI: 82.5/100)."
        };

        byte[] pdfBytes = _generator.GenerateReportPdf(model);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 2000, "Full European scouting report PDF should be well-formed and > 2KB");
        string header = Encoding.ASCII.GetString(pdfBytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void GenerateReportPdf_UnderTurkishLocale_ShouldSucceedAndMaintainStrictFormatting()
    {
        var prevCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            var model = new PlayerScoutingReportDto
            {
                PlayerId = Guid.NewGuid(),
                FirstName = "Johnathan",
                LastName = "Motley",
                Position = "C/F",
                Age = 31,
                HeightCm = 206,
                WeightKg = 104,
                TeamName = "Hapoel Tel Aviv",
                League = "EuroCup",
                Season = 2026,
                Archetype = "PaintAnchor",
                RadarMetrics = new RadarMetricsDto(88.4, 76.2, 54.1, 89.5, 68.3, 85.1),
                GamesPlayed = 24,
                Minutes = 24.2m,
                Points = 16.4m,
                Assists = 2.1m,
                Rebounds = 6.8m,
                Steals = 0.8m,
                Blocks = 1.1m,
                FgPct = 0.625m,
                Fg3Pct = 0.250m,
                FtPct = 0.742m,
                Turnovers = 2.3m,
                TrueShootingPct = 0.667m,
                UsagePct = 28.5m,
                OffensiveReboundShare = 0.124m,
                FreeThrowRate = 0.58m,
                Pir = 20.4m,
                PtsPer36 = 24.4m,
                StocksPer36 = 2.8m,
                PointsPer40 = 27.1m,
                AssistsPer40 = 3.5m,
                ReboundsPer40 = 11.2m,
                FoulsCommittedPer36 = 5.7m,
                FibaFoulTroubleRisk = "High Foul Risk",
                TwoPointScoringSharePct = 60.0,
                ThreePointScoringSharePct = 21.0,
                FreeThrowScoringSharePct = 19.0,
                EstimatedMarketValueTier = "Tier-1 ($2.5M - $15M+ NBA)",
                ContractExpirySeason = 2026,
                EuropeanCompatibilityIndex = 74.5,
                DefensivePressureIndex = 62.0,
                TacticalVerdict = "Johnathan Motley is classified as a PaintAnchor (MVP-Caliber Franchise Cornerstone).",
                CareerHistory = new List<CareerSeasonStintDto>
                {
                    new() { Season = 2024, League = "EuroLeague", TeamName = "Fenerbahce Beko", GamesPlayed = 36, Points = 14.5m, PtsPer36 = 23.1m, TrueShootingPct = 0.671m, EuroLeagueTransitionIndex = 92.4m },
                    new() { Season = 2025, League = "EuroCup", TeamName = "Hapoel Tel Aviv", GamesPlayed = 18, Points = 17.5m, PtsPer36 = 25.8m, TrueShootingPct = 0.680m, EuroLeagueTransitionIndex = 95.0m }
                }
            };

            byte[] pdfBytes = _generator.GenerateReportPdf(model);

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 2000);
            string header = Encoding.ASCII.GetString(pdfBytes, 0, 5);
            Assert.Equal("%PDF-", header);

            // Verify strict 1-page budget
            string pdfText = Encoding.ASCII.GetString(pdfBytes);
            int pageCount = System.Text.RegularExpressions.Regex.Matches(pdfText, @"/Type\s*/Page\b").Count;
            Assert.Equal(1, pageCount);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = prevCulture;
        }
    }
}
