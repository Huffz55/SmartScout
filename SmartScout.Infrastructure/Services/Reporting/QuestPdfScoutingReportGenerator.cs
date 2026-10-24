using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using System;

namespace SmartScout.Infrastructure.Services.Reporting;

public class QuestPdfScoutingReportGenerator : IScoutingReportGenerator
{
    static QuestPdfScoutingReportGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateReportPdf(PlayerScoutingReportDto model)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Lato"));

                page.Header().Element(c => ComposeHeader(c, model));
                page.Content().Element(c => ComposeContent(c, model));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, PlayerScoutingReportDto model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("SMARTSCOUT ANALYTICS").FontSize(16).Bold().FontColor(Color.FromHex("#0F172A"));
                col.Item().Text("MONEYBALL SCOUTING & PLAYER EVALUATION DOSSIER").FontSize(8).SemiBold().FontColor(Color.FromHex("#2563EB"));
            });

            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text("CONFIDENTIAL SCOUTING REPORT").FontSize(8).Bold().FontColor(Color.FromHex("#DC2626"));
                col.Item().Text($"Season: {model.Season - 1}-{model.Season.ToString().Substring(2)} ({model.League})").FontSize(9).SemiBold();
                col.Item().Text($"Generated: {model.GeneratedAt:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static void ComposeContent(IContainer container, PlayerScoutingReportDto model)
    {
        container.PaddingVertical(10).Column(col =>
        {
            // 1. Hero Player Banner
            col.Item().Background(Color.FromHex("#0F172A")).Padding(14).Row(row =>
            {
                row.RelativeItem(3).Column(c =>
                {
                    c.Item().Text(model.FullName).FontSize(20).Bold().FontColor(Colors.White);
                    c.Item().PaddingTop(3).Text($"{model.TeamName ?? "Free Agent"} | {model.Position ?? "N/A"} | League: {model.League} | Nationality: {model.Nationality ?? "INT"}")
                        .FontSize(9).FontColor(Color.FromHex("#94A3B8"));
                });

                row.RelativeItem(2).AlignRight().Column(c =>
                {
                    c.Item().Background(Color.FromHex("#2563EB")).PaddingVertical(4).PaddingHorizontal(8).Text(model.Archetype.ToUpperInvariant())
                        .FontSize(9).Bold().FontColor(Colors.White);
                    c.Item().PaddingTop(4).Text($"Impact Score: {model.RadarMetrics.OverallImpact:F1} / 100")
                        .FontSize(10).Bold().FontColor(Color.FromHex("#F59E0B"));
                });
            });

            col.Item().Height(10);

            // 2. High-Impact Stat Tiles (6 Cards)
            col.Item().Row(row =>
            {
                ComposeStatCard(row.RelativeItem(), "POINTS / G", model.Points?.ToString("F1") ?? "-");
                ComposeStatCard(row.RelativeItem(), "REBOUNDS / G", model.Rebounds?.ToString("F1") ?? "-");
                ComposeStatCard(row.RelativeItem(), "ASSISTS / G", model.Assists?.ToString("F1") ?? "-");
                ComposeStatCard(row.RelativeItem(), "TRUE SHOOTING", model.TrueShootingPct.HasValue ? $"{model.TrueShootingPct.Value * 100:F1}%" : "-");
                ComposeStatCard(row.RelativeItem(), "PIR (VALUATION)", model.Pir?.ToString("F1") ?? (model.WinShares?.ToString("F1") ?? "-"));
                ComposeStatCard(row.RelativeItem(), "FOULS DRAWN", model.FoulsDrawn?.ToString("F1") ?? (model.PlayerEfficiencyRating?.ToString("F1") ?? "-"));
            });

            col.Item().Height(12);

            // 3. Radar & Skill Breakdown
            col.Item().Text("RADAR SKILL & SYSTEM BREAKDOWN (0 - 100 NORMALIZED SCALE)").FontSize(10).Bold().FontColor(Color.FromHex("#0F172A"));
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    ComposeSkillBar(c, "Scoring Prowess", model.RadarMetrics.Scoring);
                    ComposeSkillBar(c, "Shooting Efficiency", model.RadarMetrics.ShootingEfficiency);
                    ComposeSkillBar(c, "Playmaking & Vision", model.RadarMetrics.Playmaking);
                    ComposeSkillBar(c, "European Compatibility (ECI)", model.EuropeanCompatibilityIndex ?? 50.0);
                });

                row.ConstantItem(15);

                row.RelativeItem().Column(c =>
                {
                    ComposeSkillBar(c, "Rebounding Dominance", model.RadarMetrics.Rebounding);
                    ComposeSkillBar(c, "Defensive Disruption", model.RadarMetrics.Defense);
                    ComposeSkillBar(c, "Overall Analytics Impact", model.RadarMetrics.OverallImpact);
                    ComposeSkillBar(c, "Alimpijević Pressure Index (DPI)", model.DefensivePressureIndex ?? 50.0);
                });
            });

            col.Item().Height(14);

            // 4. Per Game Statistics Table
            col.Item().Text("PER-GAME METRICS").FontSize(10).Bold().FontColor(Color.FromHex("#0F172A"));
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (int i = 0; i < 11; i++) columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    string[] headers = { "GP", "MIN", "PTS", "REB", "AST", "STL", "BLK", "FG%", "3P%", "FT%", "TOV" };
                    foreach (var h in headers)
                    {
                        header.Cell().Background(Color.FromHex("#F1F5F9")).Padding(4).AlignCenter().Text(h).FontSize(8).Bold();
                    }
                });

                string[] values = {
                    model.GamesPlayed.ToString(),
                    model.Minutes?.ToString("F1") ?? "-",
                    model.Points?.ToString("F1") ?? "-",
                    model.Rebounds?.ToString("F1") ?? "-",
                    model.Assists?.ToString("F1") ?? "-",
                    model.Steals?.ToString("F1") ?? "-",
                    model.Blocks?.ToString("F1") ?? "-",
                    model.FgPct.HasValue ? $"{model.FgPct.Value * 100:F1}%" : "-",
                    model.Fg3Pct.HasValue ? $"{model.Fg3Pct.Value * 100:F1}%" : "-",
                    model.FtPct.HasValue ? $"{model.FtPct.Value * 100:F1}%" : "-",
                    model.Turnovers?.ToString("F1") ?? "-"
                };

                foreach (var v in values)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(v).FontSize(8);
                }
            });

            col.Item().Height(12);

            // 5. Advanced Analytics & European Profile Table
            col.Item().Text("ADVANCED ANALYTICS & PER-40 NORMALIZED PROFILE").FontSize(10).Bold().FontColor(Color.FromHex("#0F172A"));
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (int i = 0; i < 8; i++) columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    string[] headers = { "TS%", "PER", "PIR", "FOULS", "PTS/40", "AST/40", "REB/40", "BPM" };
                    foreach (var h in headers)
                    {
                        header.Cell().Background(Color.FromHex("#F1F5F9")).Padding(4).AlignCenter().Text(h).FontSize(8).Bold();
                    }
                });

                string[] values = {
                    model.TrueShootingPct.HasValue ? $"{model.TrueShootingPct.Value * 100:F1}%" : "-",
                    model.PlayerEfficiencyRating?.ToString("F1") ?? "-",
                    model.Pir?.ToString("F1") ?? "-",
                    model.FoulsDrawn?.ToString("F1") ?? "-",
                    model.PointsPer40?.ToString("F1") ?? "-",
                    model.AssistsPer40?.ToString("F1") ?? "-",
                    model.ReboundsPer40?.ToString("F1") ?? "-",
                    model.BoxPlusMinus?.ToString("F1") ?? "-"
                };

                foreach (var v in values)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter().Text(v).FontSize(8);
                }
            });

            col.Item().Height(14);

            // 6. Tactical Verdict Box
            col.Item().Background(Color.FromHex("#F8FAFC")).Border(1).BorderColor(Color.FromHex("#2563EB")).Padding(10).Column(c =>
            {
                c.Item().Text("EXECUTIVE TACTICAL VERDICT").FontSize(9).Bold().FontColor(Color.FromHex("#2563EB"));
                c.Item().PaddingTop(3).Text(model.TacticalVerdict).FontSize(8.5f).LineHeight(1.2f).FontColor(Color.FromHex("#1E293B"));
            });
        });
    }

    private static void ComposeStatCard(IContainer container, string label, string value)
    {
        container.Padding(2).Background(Color.FromHex("#F8FAFC")).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(col =>
        {
            col.Item().AlignCenter().Text(label).FontSize(7).Bold().FontColor(Colors.Grey.Medium);
            col.Item().AlignCenter().Text(value).FontSize(13).Bold().FontColor(Color.FromHex("#0F172A"));
        });
    }

    private static void ComposeSkillBar(ColumnDescriptor col, string label, double value)
    {
        col.Item().PaddingBottom(5).Row(row =>
        {
            row.RelativeItem(3).Text(label).FontSize(8).SemiBold();
            row.RelativeItem(2).AlignRight().Text($"{value:F1} / 100").FontSize(8).Bold().FontColor(Color.FromHex("#2563EB"));
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text("SmartScout NBA/EuroLeague Automated Data Pipeline — For Internal Scouting Use Only")
                .FontSize(7).FontColor(Colors.Grey.Medium);

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }
}
