using System.Linq;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Interfaces;

public interface IPlayerDirectorySearchFilter
{
    IQueryable<Player> ApplySearchFilter(IQueryable<Player> query, string? searchTerm);

    IQueryable<PlayerSeasonStat> ApplyTeamSearchFilter(IQueryable<PlayerSeasonStat> query, string? searchTerm);

    static string EscapeLikePattern(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
