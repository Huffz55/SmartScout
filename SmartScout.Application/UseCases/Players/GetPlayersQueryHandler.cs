using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.DTOs;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.UseCases.Players;

public class GetPlayersQueryHandler : IRequestHandler<GetPlayersQuery, PagedResult<Player>>
{
    private readonly ISmartScoutDbContext _dbContext;

    public GetPlayersQueryHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<Player>> Handle(GetPlayersQuery request, CancellationToken cancellationToken)
    {
        int page = request.Page <= 0 ? 1 : request.Page;
        int pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 100);

        var query = _dbContext.Players.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string pattern = $"%{request.SearchTerm.Trim().ToLower()}%";
            query = query.Where(p => 
                EF.Functions.Like(p.FirstName.ToLower(), pattern) || 
                EF.Functions.Like(p.LastName.ToLower(), pattern) ||
                (p.TeamName != null && EF.Functions.Like(p.TeamName.ToLower(), pattern)));
        }

        int totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Player>(items, totalCount, page, pageSize);
    }
}
