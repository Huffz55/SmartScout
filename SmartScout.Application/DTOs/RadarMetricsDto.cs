namespace SmartScout.Application.DTOs;

public record RadarMetricsDto(
    double Scoring,
    double ShootingEfficiency,
    double Playmaking,
    double Rebounding,
    double Defense,
    double OverallImpact);
