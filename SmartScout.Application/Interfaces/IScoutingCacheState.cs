namespace SmartScout.Application.Interfaces;

/// <summary>
/// Tracks a monotonically increasing version token for cached Sleeper & scouting analytics datasets.
/// Incremented whenever season stats, injuries, watchlist flags, or player bios change.
/// </summary>
public interface IScoutingCacheState
{
    long Version { get; }
    long BumpVersion();
}
