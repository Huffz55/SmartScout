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
}
