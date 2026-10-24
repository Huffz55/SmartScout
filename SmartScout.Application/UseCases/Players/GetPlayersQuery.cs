using MediatR;
using SmartScout.Application.DTOs;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.UseCases.Players;

public class GetPlayersQuery : IRequest<PagedResult<Player>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SearchTerm { get; set; }
}
