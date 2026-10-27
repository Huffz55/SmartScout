using MediatR;
using SmartScout.Application.DTOs;
using System;

namespace SmartScout.Application.UseCases.Analytics;

public class GetPlayerProfileQuery : IRequest<PlayerScoutingReportDto?>
{
    public Guid PlayerId { get; set; }
    public int? Season { get; set; }
}
