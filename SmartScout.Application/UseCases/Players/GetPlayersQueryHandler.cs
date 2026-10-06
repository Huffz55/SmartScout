using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.Players;

public class GetPlayersQueryHandler : IRequestHandler<GetPlayersQuery, List<Player>>
{
    private readonly ISmartScoutDbContext _dbContext;

    public GetPlayersQueryHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Player>> Handle(GetPlayersQuery request, CancellationToken cancellationToken)
    {
        return await _dbContext.Players.ToListAsync(cancellationToken);
    }
}
