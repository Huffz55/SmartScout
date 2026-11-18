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

        bool useNpgsqlILike = _dbContext is DbContext efDb
            && (efDb.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false);

        var query = ApplySearchFilter(_dbContext.Players.AsNoTracking(), request.SearchTerm, useNpgsqlILike);

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

    public static IQueryable<Player> ApplySearchFilter(IQueryable<Player> query, string? searchTerm, bool useNpgsqlILike = true)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return query;
        }

        string rawTrimmed = searchTerm.Trim();
        string normalizedTerm = Player.NormalizeName(rawTrimmed, string.Empty);

        if (useNpgsqlILike)
        {
            string rawPattern = $"%{EscapeLikePattern(rawTrimmed)}%";
            string? normPattern = string.IsNullOrWhiteSpace(normalizedTerm)
                ? null
                : $"%{EscapeLikePattern(normalizedTerm)}%";

            return query.Where(p =>
                (normPattern != null && EF.Functions.ILike(p.NormalizedName, normPattern, "\\")) ||
                EF.Functions.ILike(p.FirstName, rawPattern, "\\") ||
                EF.Functions.ILike(p.LastName, rawPattern, "\\") ||
                (p.TeamName != null && EF.Functions.ILike(p.TeamName, rawPattern, "\\")));
        }

        return query.Where(p =>
            (!string.IsNullOrWhiteSpace(normalizedTerm) && p.NormalizedName.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase)) ||
            p.FirstName.Contains(rawTrimmed, StringComparison.OrdinalIgnoreCase) ||
            p.LastName.Contains(rawTrimmed, StringComparison.OrdinalIgnoreCase) ||
            (p.TeamName != null && p.TeamName.Contains(rawTrimmed, StringComparison.OrdinalIgnoreCase)));
    }
}
