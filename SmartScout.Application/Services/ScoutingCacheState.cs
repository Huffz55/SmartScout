using System.Threading;
using SmartScout.Application.Interfaces;

namespace SmartScout.Application.Services;

public sealed class ScoutingCacheState : IScoutingCacheState
{
    private long _version = 1;

    public long Version => Interlocked.Read(ref _version);

    public long BumpVersion() => Interlocked.Increment(ref _version);
}
