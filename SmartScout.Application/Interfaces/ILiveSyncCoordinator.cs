using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.Interfaces;

public static class SyncCheckpointConstants
{
    public const string InjuryCheckpointCode = "__LiveInjuries";
}

public record LiveSyncCycleResult(
    bool InjurySyncExecuted,
    int InjuryMatchedCount,
    bool StatSyncExecuted,
    int StatsUpsertedCount,
    TimeSpan RecommendedNextDelay,
    int BioEnrichedCount = 0);

public interface ILiveSyncCoordinator
{
    Task<LiveSyncCycleResult> RunSyncCycleAsync(DateTime utcNow, CancellationToken cancellationToken = default);
}
