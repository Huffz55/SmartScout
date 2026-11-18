using System.Linq;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
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
        string rawPattern = $"%{IPlayerDirectorySearchFilter.EscapeLikePattern(rawTrimmed)}%";
        string normalizedTerm = Player.NormalizeName(rawTrimmed, string.Empty);
        string? normPattern = string.IsNullOrWhiteSpace(normalizedTerm)
            ? null
            : $"%{IPlayerDirectorySearchFilter.EscapeLikePattern(normalizedTerm)}%";

        return query.Where(p =>
            (normPattern != null && EF.Functions.ILike(p.NormalizedName, normPattern, "\\")) ||
            EF.Functions.ILike(p.FirstName, rawPattern, "\\") ||
            EF.Functions.ILike(p.LastName, rawPattern, "\\") ||
            (p.TeamName != null && EF.Functions.ILike(p.TeamName, rawPattern, "\\")));
    }

    public IQueryable<PlayerSeasonStat> ApplyTeamSearchFilter(IQueryable<PlayerSeasonStat> query, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return query;
        }

        string pattern = $"%{IPlayerDirectorySearchFilter.EscapeLikePattern(searchTerm.Trim())}%";
        return query.Where(s => EF.Functions.ILike(s.TeamName!, pattern, "\\") || EF.Functions.ILike(s.League, pattern, "\\"));
    }
}
