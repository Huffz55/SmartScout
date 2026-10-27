using System.Collections.Generic;
using MediatR;
using SmartScout.Application.DTOs;

namespace SmartScout.Application.UseCases.Analytics;

public class GetSleeperPlayersQuery : IRequest<List<SleeperPlayerDto>>
{
    public string? Category { get; set; }
    public string? League { get; set; }
    public int? Season { get; set; }
    public int Top { get; set; } = 25;
}
