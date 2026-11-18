namespace SmartScout.Application.Interfaces;

/// <summary>
/// Tracks a monotonically increasing version token for cached Sleeper & scouting analytics datasets.
/// Incremented whenever season stats, injuries, watchlist flags, or player bios change.
/// </summary>
public interface IScoutingCacheState
{
    long Version { get; }
    long BumpVersion();

    string BuildKey(string prefix, params object?[] parts) => BuildVersionedKey(Version, prefix, parts);

    static string BuildVersionedKey(long version, string prefix, params object?[] parts) =>
        $"{prefix}_v{version}|{string.Join('|', parts)}";
}
