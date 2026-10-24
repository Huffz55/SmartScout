using MediatR;
using System;

namespace SmartScout.Application.UseCases.Analytics;

public class GetPlayerScoutingReportQuery : IRequest<byte[]>
{
    public Guid PlayerId { get; set; }
    public int Season { get; set; }
}
