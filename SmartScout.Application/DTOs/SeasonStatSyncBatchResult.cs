using System;
using System.Collections.Generic;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.DTOs;

public class SeasonStatSyncBatchResult
{
    public IReadOnlyList<PlayerSeasonStat> Stats { get; init; } = Array.Empty<PlayerSeasonStat>();
    public IReadOnlyList<Guid> PhantomPlayerIdsToDelete { get; init; } = Array.Empty<Guid>();

    public SeasonStatSyncBatchResult(
        IReadOnlyList<PlayerSeasonStat> stats, 
        IReadOnlyList<Guid> phantomPlayerIdsToDelete)
    {
        Stats = stats;
        PhantomPlayerIdsToDelete = phantomPlayerIdsToDelete;
    }
}
