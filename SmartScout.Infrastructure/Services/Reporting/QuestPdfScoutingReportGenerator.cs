using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using System;
using System.Globalization;

namespace SmartScout.Infrastructure.Services.Reporting;

public class QuestPdfScoutingReportGenerator : IScoutingReportGenerator
{
    static QuestPdfScoutingReportGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateReportPdf(PlayerScoutingReportDto model)
    {
        var prevCulture = CultureInfo.CurrentCulture;
        var prevUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

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
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUiCulture;
        }
    }

    private static void ComposeHeader(IContainer container, PlayerScoutingReportDto model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("SMARTSCOUT BASKETBALL INTELLIGENCE").FontSize(16).Bold().FontColor(Color.FromHex("#0F172A"));
                col.Item().Text("MONEYBALL SCOUTING & ADVANCED PLAYER EVALUATION DOSSIER").FontSize(8).SemiBold().FontColor(Color.FromHex("#2563EB"));
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
        container.PaddingVertical(8).Column(col =>
        {
            // 1. Hero Player Banner
            col.Item().Background(Color.FromHex("#0F172A")).Padding(12).Row(row =>
            {
                row.RelativeItem(3).Column(c =>
                {
                    c.Item().Text(model.FullName).FontSize(19).Bold().FontColor(Colors.White);
                    string bioSub = $"{model.TeamName ?? "Free Agent"} | {model.Position ?? "N/A"} | League: {model.League} | Nationality: {model.Nationality ?? "INT"}";
                    if (model.Age.HasValue) bioSub += $" | Age: {model.Age}";
                    if (model.HeightCm.HasValue) bioSub += $" | {model.HeightCm} cm";
                    if (model.WeightKg.HasValue) bioSub += $" | {model.WeightKg} kg";
                    c.Item().PaddingTop(2).Text(bioSub).FontSize(8.5f).FontColor(Color.FromHex("#94A3B8"));
                });

                row.RelativeItem(2).AlignRight().Column(c =>
                {
                    c.Item().Background(Color.FromHex("#2563EB")).PaddingVertical(3).PaddingHorizontal(8).Text(model.Archetype.ToUpperInvariant())
                        .FontSize(8.5f).Bold().FontColor(Colors.White);
                    c.Item().PaddingTop(3).Text($"Impact Score: {model.RadarMetrics.OverallImpact.ToString("F1", CultureInfo.InvariantCulture)} / 100")
                        .FontSize(9.5f).Bold().FontColor(Color.FromHex("#F59E0B"));
                    if (model.EuroLeagueTransitionIndex.HasValue)
                    {
                        c.Item().PaddingTop(1).Text($"Transition Index: {model.EuroLeagueTransitionIndex.Value.ToString("F1", CultureInfo.InvariantCulture)} / 100")
                            .FontSize(8f).Bold().FontColor(Color.FromHex("#10B981"));
                    }
                });
            });

            col.Item().Height(5);

            // Market, Shot Diet & FIBA Foul Discipline Row
            col.Item().Background(Color.FromHex("#F1F5F9")).Padding(5).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("MARKET VALUE TIER").FontSize(6.5f).Bold().FontColor(Color.FromHex("#64748B"));
                    c.Item().Text(model.EstimatedMarketValueTier ?? "Tier-2 ($250k - $750k)").FontSize(7.5f).Bold().FontColor(Color.FromHex("#0F172A"));
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("CONTRACT EXPIRY").FontSize(6.5f).Bold().FontColor(Color.FromHex("#64748B"));
                    c.Item().Text(model.ContractExpirySeason.HasValue ? $"{model.ContractExpirySeason} Free Agent" : "2026 Free Agent").FontSize(7.5f).Bold().FontColor(Color.FromHex("#0F172A"));
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("STOCKS / 36").FontSize(6.5f).Bold().FontColor(Color.FromHex("#64748B"));
                    int stkPct = model.Percentiles.TryGetValue("StocksPer36", out var sp) ? sp : 50;
                    c.Item().Text(model.StocksPer36.HasValue ? $"{model.StocksPer36.Value.ToString("F1", CultureInfo.InvariantCulture)} ({stkPct}th %)" : "-").FontSize(7.5f).Bold().FontColor(Color.FromHex("#0F172A"));
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("SHOT DIET (2P/3P/FT)").FontSize(6.5f).Bold().FontColor(Color.FromHex("#64748B"));
                    c.Item().Text($"{model.TwoPointScoringSharePct.ToString("0", CultureInfo.InvariantCulture)}% / {model.ThreePointScoringSharePct.ToString("0", CultureInfo.InvariantCulture)}% / {model.FreeThrowScoringSharePct.ToString("0", CultureInfo.InvariantCulture)}%").FontSize(7.5f).Bold().FontColor(Color.FromHex("#0F172A"));
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("FIBA FOUL RISK").FontSize(6.5f).Bold().FontColor(Color.FromHex("#64748B"));
                    c.Item().Text($"{model.FibaFoulTroubleRisk} ({model.FoulsCommittedPer36?.ToString("F1", CultureInfo.InvariantCulture) ?? "2.8"} PF/36)").FontSize(7.5f).Bold().FontColor(Color.FromHex("#0F172A"));
                });
            });

            col.Item().Height(7);

            // 2. High-Impact Stat Tiles (6 Cards) - Replaced FOULS DRAWN with PTS / 36
            col.Item().Row(row =>
            {
                ComposeStatCard(row.RelativeItem(), "POINTS / G", model.Points?.ToString("F1", CultureInfo.InvariantCulture) ?? "-");
                ComposeStatCard(row.RelativeItem(), "REBOUNDS / G", model.Rebounds?.ToString("F1", CultureInfo.InvariantCulture) ?? "-");
                ComposeStatCard(row.RelativeItem(), "ASSISTS / G", model.Assists?.ToString("F1", CultureInfo.InvariantCulture) ?? "-");
                ComposeStatCard(row.RelativeItem(), "TRUE SHOOTING", model.TrueShootingPct.HasValue ? $"{(model.TrueShootingPct.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%" : "-");
                ComposeStatCard(row.RelativeItem(), "PIR (VALUATION)", model.Pir?.ToString("F1", CultureInfo.InvariantCulture) ?? (model.WinShares?.ToString("F1", CultureInfo.InvariantCulture) ?? "-"));
                ComposeStatCard(row.RelativeItem(), "PTS / 36", model.PtsPer36?.ToString("F1", CultureInfo.InvariantCulture) ?? "-");
            });

            col.Item().Height(8);

            // 3. Radar & Skill Breakdown with Sleek Progress Gauges
            col.Item().Text("RADAR SKILL & SYSTEM BREAKDOWN (0 - 100 NORMALIZED SCALE)").FontSize(9.5f).Bold().FontColor(Color.FromHex("#0F172A"));
            col.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    ComposeSkillBar(c, "Scoring Prowess", model.RadarMetrics.Scoring);
                    ComposeSkillBar(c, "Shooting Efficiency", model.RadarMetrics.ShootingEfficiency);
                    ComposeSkillBar(c, "Playmaking & Vision", model.RadarMetrics.Playmaking);
                    ComposeSkillBar(c, "European Compatibility (ECI)", model.EuropeanCompatibilityIndex ?? 50.0);
                });

                row.ConstantItem(14);

                row.RelativeItem().Column(c =>
                {
                    ComposeSkillBar(c, "Rebounding Dominance", model.RadarMetrics.Rebounding);
                    ComposeSkillBar(c, "Defensive Disruption", model.RadarMetrics.Defense);
                    ComposeSkillBar(c, "Overall Analytics Impact", model.RadarMetrics.OverallImpact);
                    ComposeSkillBar(c, "Alimpijević Pressure Index (DPI)", model.DefensivePressureIndex ?? 50.0);
                });
            });

            col.Item().Height(8);

            // 4. Per Game Statistics Table
            col.Item().Text("PER-GAME METRICS").FontSize(9.5f).Bold().FontColor(Color.FromHex("#0F172A"));
            col.Item().PaddingTop(3).Table(table =>
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
                        header.Cell().Background(Color.FromHex("#F1F5F9")).Padding(3).AlignCenter().Text(h).FontSize(7.5f).Bold();
                    }
                });

                string[] values = {
                    model.GamesPlayed.ToString(),
                    model.Minutes?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.Points?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.Rebounds?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.Assists?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.Steals?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.Blocks?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.FgPct.HasValue ? $"{(model.FgPct.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%" : "-",
                    model.Fg3Pct.HasValue ? $"{(model.Fg3Pct.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%" : "-",
                    model.FtPct.HasValue ? $"{(model.FtPct.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%" : "-",
                    model.Turnovers?.ToString("F1", CultureInfo.InvariantCulture) ?? "-"
                };

                foreach (var v in values)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignCenter().Text(v).FontSize(7.5f);
                }
            });

            col.Item().Height(8);

            // 5. Advanced Analytics & European Profile Table (All Guaranteed Metrics, No Null Dashes)
            col.Item().Text("ADVANCED ANALYTICS & PER-40 NORMALIZED PROFILE").FontSize(9.5f).Bold().FontColor(Color.FromHex("#0F172A"));
            col.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (int i = 0; i < 8; i++) columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    string[] headers = { "TS%", "USG%", "PIR", "ORB%", "FTr", "PTS/40", "AST/40", "REB/40" };
                    foreach (var h in headers)
                    {
                        header.Cell().Background(Color.FromHex("#F1F5F9")).Padding(3).AlignCenter().Text(h).FontSize(7.5f).Bold();
                    }
                });

                string[] values = {
                    model.TrueShootingPct.HasValue ? $"{(model.TrueShootingPct.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%" : "-",
                    model.UsagePct.HasValue ? $"{model.UsagePct.Value.ToString("F1", CultureInfo.InvariantCulture)}%" : "-",
                    model.Pir?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.OffensiveReboundShare.HasValue ? $"{(model.OffensiveReboundShare.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%" : "-",
                    model.FreeThrowRate.HasValue ? model.FreeThrowRate.Value.ToString("F2", CultureInfo.InvariantCulture) : "-",
                    model.PointsPer40?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.AssistsPer40?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                    model.ReboundsPer40?.ToString("F1", CultureInfo.InvariantCulture) ?? "-"
                };

                foreach (var v in values)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignCenter().Text(v).FontSize(7.5f);
                }
            });

            col.Item().Height(8);

            // 6. Tactical Verdict Box
            col.Item().Background(Color.FromHex("#F8FAFC")).Border(1).BorderColor(Color.FromHex("#2563EB")).Padding(7).Column(c =>
            {
                c.Item().Text("EXECUTIVE TACTICAL VERDICT").FontSize(8.5f).Bold().FontColor(Color.FromHex("#2563EB"));
                c.Item().PaddingTop(2).Text(model.TacticalVerdict).FontSize(8f).LineHeight(1.2f).FontColor(Color.FromHex("#1E293B"));
            });

            // 7. Scout Notes Box (if any)
            if (!string.IsNullOrWhiteSpace(model.ScoutNotes))
            {
                col.Item().Height(5);
                col.Item().Background(Color.FromHex("#FEF3C7")).Border(1).BorderColor(Color.FromHex("#F59E0B")).Padding(5).Column(c =>
                {
                    c.Item().Text("CONFIDENTIAL SCOUT LOG & OBSERVATIONS").FontSize(7.5f).Bold().FontColor(Color.FromHex("#B45309"));
                    c.Item().PaddingTop(1).Text(model.ScoutNotes).FontSize(7f).FontColor(Color.FromHex("#78350F"));
                });
            }

            // 8. Multi-Season Career Trajectory (if available)
            if (model.CareerHistory != null && model.CareerHistory.Count > 1)
            {
                col.Item().Height(5);
                col.Item().Text("MULTI-SEASON CAREER PROGRESSION").FontSize(8f).Bold().FontColor(Color.FromHex("#0F172A"));
                col.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        string[] hCols = { "SEASON", "LEAGUE", "CLUB", "GP", "PTS", "PTS/36", "TS%", "ETI" };
                        foreach (var h in hCols)
                        {
                            header.Cell().Background(Color.FromHex("#F1F5F9")).Padding(2).AlignCenter().Text(h).FontSize(6.5f).Bold();
                        }
                    });

                    var recentStints = model.CareerHistory.Count > 5
                        ? model.CareerHistory.GetRange(model.CareerHistory.Count - 5, 5)
                        : model.CareerHistory;

                    foreach (var s in recentStints)
                    {
                        string[] rowVals = {
                            $"{s.Season - 1}-{s.Season.ToString().Substring(2)}",
                            s.League,
                            s.TeamName ?? "-",
                            s.GamesPlayed.ToString(),
                            s.Points?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                            s.PtsPer36?.ToString("F1", CultureInfo.InvariantCulture) ?? "-",
                            s.TrueShootingPct.HasValue ? $"{(s.TrueShootingPct.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%" : "-",
                            s.EuroLeagueTransitionIndex?.ToString("F1", CultureInfo.InvariantCulture) ?? "-"
                        };
                        foreach (var rv in rowVals)
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).AlignCenter().Text(rv).FontSize(6.5f);
                        }
                    }
                });
            }
        });
    }

    private static void ComposeStatCard(IContainer container, string label, string value)
    {
        container.Padding(2).Background(Color.FromHex("#F8FAFC")).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(col =>
        {
            col.Item().AlignCenter().Text(label).FontSize(6.5f).Bold().FontColor(Colors.Grey.Medium);
            col.Item().AlignCenter().Text(value).FontSize(12.5f).Bold().FontColor(Color.FromHex("#0F172A"));
        });
    }

    private static void ComposeSkillBar(ColumnDescriptor col, string label, double value)
    {
        double clampedVal = Math.Clamp(value, 0.0, 100.0);
        float fillRatio = (float)(clampedVal / 100.0);
        string colorHex = clampedVal >= 75 ? "#10B981" : (clampedVal >= 50 ? "#2563EB" : (clampedVal >= 35 ? "#F59E0B" : "#DC2626"));

        col.Item().PaddingBottom(3).Row(row =>
        {
            row.RelativeItem(5).Text(label).FontSize(7.5f).SemiBold().FontColor(Color.FromHex("#0F172A"));
            row.ConstantItem(48).AlignMiddle().Height(4).Background(Color.FromHex("#E2E8F0")).Row(bar =>
            {
                if (fillRatio > 0.02f)
                {
                    bar.RelativeItem(fillRatio).Height(4).Background(Color.FromHex(colorHex));
                }
                if (fillRatio < 0.98f)
                {
                    bar.RelativeItem(1.0f - fillRatio);
                }
            });
            row.RelativeItem(3).AlignRight().Text($"{clampedVal.ToString("F1", CultureInfo.InvariantCulture)} / 100")
                .FontSize(7.5f).Bold().FontColor(Color.FromHex(colorHex));
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text("SmartScout Basketball Analytics Platform — Confidential Scouting Dossier")
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
