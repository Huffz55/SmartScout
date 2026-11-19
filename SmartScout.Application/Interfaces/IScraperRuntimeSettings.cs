namespace SmartScout.Application.Interfaces;

public interface IScraperRuntimeSettings
{
    bool EnableBackgroundSync { get; }
    bool EnableLiveInjuryAutoSync { get; }
    int InjurySyncIntervalHours { get; }
    int CurrentSeasonStatRefreshHours { get; }
    int LiveSeasonYear { get; }
    int StartupSyncDelaySeconds { get; }
}
