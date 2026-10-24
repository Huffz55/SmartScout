using SmartScout.Application.DTOs;
using SmartScout.Domain.Entities;
using SmartScout.Domain.Enums;
using System.Collections.Generic;

namespace SmartScout.Application.Interfaces;

public interface IPlayerAnalyticsService
{
    double CalculateSimilarity(PlayerSeasonStat target, PlayerSeasonStat candidate);
    PlayerArchetype DetermineArchetype(PlayerSeasonStat stat, string? position = null, string? height = null);
    RadarMetricsDto CalculateRadarMetrics(PlayerSeasonStat stat);
    PlayerComparisonDto Compare(PlayerSeasonStat statA, Player playerA, PlayerSeasonStat statB, Player playerB);
    double CalculateEuropeanCompatibilityIndex(PlayerSeasonStat stat);
    double CalculateDefensivePressureIndex(PlayerSeasonStat stat);
}
