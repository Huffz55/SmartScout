using SmartScout.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.Interfaces;

public interface IPlayerSyncStrategy
{
    IAsyncEnumerable<IEnumerable<Player>> FetchPlayersAsync(int cursor, CancellationToken cancellationToken);
}
