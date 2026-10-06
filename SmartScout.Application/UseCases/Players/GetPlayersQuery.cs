using MediatR;
using SmartScout.Domain.Entities;
using System.Collections.Generic;

namespace SmartScout.Application.UseCases.Players;

public class GetPlayersQuery : IRequest<List<Player>>
{
}
