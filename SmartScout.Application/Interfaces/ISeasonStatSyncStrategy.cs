using System.Collections.Generic;
using System.Threading;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Interfaces;

public interface ISeasonStatSyncStrategy
{
    IAsyncEnumerable<IEnumerable<PlayerSeasonStat>> FetchSeasonStatsAsync(List<Player> players, CancellationToken cancellationToken);
}
