using MediatR;
using SmartScout.Application.DTOs;
using System;
using System.Collections.Generic;

namespace SmartScout.Application.UseCases.Analytics;

public class GetSimilarPlayersQuery : IRequest<List<SimilarPlayerDto>>
{
    public Guid PlayerId { get; set; }
    public int Season { get; set; }
    public int Top { get; set; } = 5;
    public int MinGamesPlayed { get; set; } = 20;
    public bool ExcludeSamePlayer { get; set; } = true;
}
