using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.Interfaces;

public record SyncedInjuryEntryDto(
    string PlayerName,
    string League,
    string InjuryType,
    string Status,
    string Source,
    string? Notes);

public record InjurySyncSummaryDto(
    int TotalFetched,
    int MatchedPlayers,
    int InsertedCount,
    int UpdatedCount,
    int RecoveredMarkedCount,
    Dictionary<string, int> SourceCounts,
    List<SyncedInjuryEntryDto> SampleMatched);

public interface IPlayerInjurySyncService
{
    Task<InjurySyncSummaryDto> SyncLiveInjuriesAsync(CancellationToken cancellationToken = default);
}
