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
    private readonly IPlayerDirectorySearchFilter? _searchFilter;

    public GetPlayersQueryHandler(
        ISmartScoutDbContext dbContext,
        IPlayerDirectorySearchFilter? searchFilter = null)
    {
        _dbContext = dbContext;
        _searchFilter = searchFilter;
    }

    public async Task<PagedResult<Player>> Handle(GetPlayersQuery request, CancellationToken cancellationToken)
    {
        int page = request.Page <= 0 ? 1 : request.Page;
        int pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 100);

        var query = _searchFilter != null
            ? _searchFilter.ApplySearchFilter(_dbContext.Players.AsNoTracking(), request.SearchTerm)
            : ApplyFallbackSearchFilter(_dbContext.Players.AsNoTracking(), request.SearchTerm);

        int totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Player>(items, totalCount, page, pageSize);
    }

    public static string EscapeLikePattern(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static IQueryable<Player> ApplyFallbackSearchFilter(IQueryable<Player> query, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return query;
        }

        string rawTrimmed = searchTerm.Trim();
        string normalizedTerm = Player.NormalizeName(rawTrimmed, string.Empty);

        return query.Where(p =>
            (!string.IsNullOrWhiteSpace(normalizedTerm) && p.NormalizedName.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase)) ||
            p.FirstName.Contains(rawTrimmed, StringComparison.OrdinalIgnoreCase) ||
            p.LastName.Contains(rawTrimmed, StringComparison.OrdinalIgnoreCase) ||
            (p.TeamName != null && p.TeamName.Contains(rawTrimmed, StringComparison.OrdinalIgnoreCase)));
    }
}
