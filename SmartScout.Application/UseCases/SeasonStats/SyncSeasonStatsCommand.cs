using MediatR;

namespace SmartScout.Application.UseCases.SeasonStats;

public class SyncSeasonStatsCommand : IRequest<int>
{
    public string? League { get; set; }
    public string? Season { get; set; }
    public bool Force { get; set; } = false;
}
