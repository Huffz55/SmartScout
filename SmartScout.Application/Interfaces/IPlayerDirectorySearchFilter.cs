using System.Linq;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Interfaces;

public interface IPlayerDirectorySearchFilter
{
    IQueryable<Player> ApplySearchFilter(IQueryable<Player> query, string? searchTerm);
}
