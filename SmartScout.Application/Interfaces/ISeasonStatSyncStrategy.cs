using System.Collections.Generic;
using System.Threading;
using SmartScout.Application.DTOs;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Interfaces;

public interface ISeasonStatSyncStrategy
{
    IAsyncEnumerable<SeasonStatSyncBatchResult> FetchSeasonStatsAsync(List<Player> players, CancellationToken cancellationToken);
}
