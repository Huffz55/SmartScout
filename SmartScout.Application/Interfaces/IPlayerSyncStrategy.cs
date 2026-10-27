using SmartScout.Application.DTOs;
using SmartScout.Domain.Entities;
using System.Collections.Generic;
using System.Threading;

namespace SmartScout.Application.Interfaces;

public interface IPlayerSyncStrategy
{
    IAsyncEnumerable<IEnumerable<Player>> FetchPlayersAsync(
        int cursor, 
        CancellationToken cancellationToken,
        SyncFilterContext? filter = null);
}
