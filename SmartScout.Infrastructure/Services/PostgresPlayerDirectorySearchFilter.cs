using System.Linq;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
using SmartScout.Application.UseCases.Players;
using SmartScout.Domain.Entities;

namespace SmartScout.Infrastructure.Services;

public class PostgresPlayerDirectorySearchFilter : IPlayerDirectorySearchFilter
{
    public IQueryable<Player> ApplySearchFilter(IQueryable<Player> query, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return query;
        }

        string rawTrimmed = searchTerm.Trim();
        string rawPattern = $"%{GetPlayersQueryHandler.EscapeLikePattern(rawTrimmed)}%";
        string normalizedTerm = Player.NormalizeName(rawTrimmed, string.Empty);
        string? normPattern = string.IsNullOrWhiteSpace(normalizedTerm)
            ? null
            : $"%{GetPlayersQueryHandler.EscapeLikePattern(normalizedTerm)}%";

        return query.Where(p =>
            (normPattern != null && EF.Functions.ILike(p.NormalizedName, normPattern, "\\")) ||
            EF.Functions.ILike(p.FirstName, rawPattern, "\\") ||
            EF.Functions.ILike(p.LastName, rawPattern, "\\") ||
            (p.TeamName != null && EF.Functions.ILike(p.TeamName, rawPattern, "\\")));
    }
}
