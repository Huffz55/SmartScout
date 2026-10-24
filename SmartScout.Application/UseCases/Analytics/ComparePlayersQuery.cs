using MediatR;
using SmartScout.Application.DTOs;
using System;

namespace SmartScout.Application.UseCases.Analytics;

public class ComparePlayersQuery : IRequest<PlayerComparisonDto>
{
    public Guid PlayerAId { get; set; }
    public int SeasonA { get; set; }
    public Guid PlayerBId { get; set; }
    public int SeasonB { get; set; }
}
