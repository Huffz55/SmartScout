using MediatR;
using SmartScout.Application.DTOs;
using System.Collections.Generic;

namespace SmartScout.Application.UseCases.Analytics;

public class SmartScoutSearchQuery : IRequest<List<SmartScoutSearchResultDto>>
{
    public string Prompt { get; set; } = string.Empty;
    public int? Season { get; set; }
    public int Top { get; set; } = 15;
}
