using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartScout.Application.Interfaces;
using SmartScout.Application.UseCases.SeasonStats;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Options;

namespace SmartScout.Infrastructure.Services;

public record LiveSyncCycleResult(
    bool InjurySyncExecuted,
    int InjuryMatchedCount,
    bool StatSyncExecuted,
    int StatsUpsertedCount,
    TimeSpan RecommendedNextDelay);

/// <summary>
/// Coordinates staleness-aware live injury syncing and active-season statistical refreshes
/// so repeated API restarts do not redundantly re-scrape fresh external feeds.
/// </summary>
public class LiveSyncCoordinator
{
    public const string InjuryCheckpointCode = "__LiveInjuries";

    private readonly ISmartScoutDbContext _dbContext;
    private readonly IPlayerInjurySyncService _injurySyncService;
    private readonly IMediator _mediator;
    private readonly ScraperOptions _options;
    private readonly ILogger<LiveSyncCoordinator> _logger;

    public LiveSyncCoordinator(
        ISmartScoutDbContext dbContext,
        IPlayerInjurySyncService injurySyncService,
        IMediator mediator,
        IOptions<ScraperOptions> options,
        ILogger<LiveSyncCoordinator> logger)
    {
        _dbContext = dbContext;
        _injurySyncService = injurySyncService;
        _mediator = mediator;
        _options = options.Value;
        _logger = logger;
    }

    internal record SyncScheduleDecision(
        bool RunInjurySync,
        bool RunStatSync,
        bool StatSyncAllBaseline,
        TimeSpan NextDelayIfClean);

    internal static SyncScheduleDecision EvaluateSchedule(
        DateTime utcNow,
        DateTime? lastInjuryAt,
        DateTime? lastStatAt,
        ScraperOptions options)
    {
        TimeSpan injuryInterval = TimeSpan.FromHours(Math.Clamp(options.InjurySyncIntervalHours, 1, 72));
        TimeSpan statInterval = TimeSpan.FromHours(Math.Clamp(options.CurrentSeasonStatRefreshHours, 1, 168));

        bool runInjury = options.EnableLiveInjuryAutoSync &&
            (lastInjuryAt == null || (utcNow - lastInjuryAt.Value) >= injuryInterval);

        bool runStat = options.EnableBackgroundSync &&
            (lastStatAt == null || (utcNow - lastStatAt.Value) >= statInterval);

        bool baseline = options.EnableBackgroundSync && lastStatAt == null;

        TimeSpan shortest = TimeSpan.FromHours(4);
        if (options.EnableLiveInjuryAutoSync)
        {
            var effectiveInjuryAt = runInjury ? utcNow : lastInjuryAt!.Value;
            var rem = (effectiveInjuryAt + injuryInterval) - utcNow;
            if (rem > TimeSpan.Zero && rem < shortest) shortest = rem;
        }
        if (options.EnableBackgroundSync)
        {
            var effectiveStatAt = runStat ? utcNow : lastStatAt!.Value;
            var rem = (effectiveStatAt + statInterval) - utcNow;
            if (rem > TimeSpan.Zero && rem < shortest) shortest = rem;
        }

        if (shortest < TimeSpan.FromMinutes(5)) shortest = TimeSpan.FromMinutes(5);
        return new SyncScheduleDecision(runInjury, runStat, baseline, shortest);
    }

    public async Task<LiveSyncCycleResult> RunSyncCycleAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        bool injuryExecuted = false;
        int injuryMatched = 0;
        bool statExecuted = false;
        int statsUpserted = 0;
        bool hadError = false;

        IngestionJobLog? injuryLog = null;
        DateTime? lastInjuryAt = null;
        if (_options.EnableLiveInjuryAutoSync)
        {
            injuryLog = await _dbContext.IngestionJobLogs
                .FirstOrDefaultAsync(l => l.LeagueCode == InjuryCheckpointCode && l.Season == _options.LiveSeasonYear, cancellationToken);
            lastInjuryAt = injuryLog?.Status == "Completed" ? injuryLog.UpdatedAt : null;
        }

        DateTime? lastStatAt = null;
        if (_options.EnableBackgroundSync)
        {
            lastStatAt = await _dbContext.IngestionJobLogs
                .AsNoTracking()
                .Where(l => l.Season == _options.LiveSeasonYear && l.LeagueCode != InjuryCheckpointCode && l.Status == "Completed")
                .MaxAsync(l => (DateTime?)l.UpdatedAt, cancellationToken);
        }

        var plan = EvaluateSchedule(utcNow, lastInjuryAt, lastStatAt, _options);

        if (plan.RunInjurySync)
        {
            try
            {
                var summary = await _injurySyncService.SyncLiveInjuriesAsync(cancellationToken);
                injuryExecuted = true;
                injuryMatched = summary.MatchedPlayers;

                if (injuryLog == null)
                {
                    injuryLog = new IngestionJobLog(InjuryCheckpointCode, _options.LiveSeasonYear, "Completed", summary.MatchedPlayers);
                    await _dbContext.IngestionJobLogs.AddAsync(injuryLog, cancellationToken);
                }
                else
                {
                    injuryLog.MarkCompleted(summary.MatchedPlayers);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Scheduled injury sync completed: {Fetched} fetched -> {Matched} matched ({Inserted} inserted, {Updated} updated, {Recovered} recovered).",
                    summary.TotalFetched, summary.MatchedPlayers, summary.InsertedCount, summary.UpdatedCount, summary.RecoveredMarkedCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                hadError = true;
                _logger.LogError(ex, "Scheduled live injury sync failed cleanly; continuing to stat refresh check.");
            }
        }

        if (plan.RunStatSync)
        {
            try
            {
                var command = plan.StatSyncAllBaseline
                    ? new SyncSeasonStatsCommand()
                    : new SyncSeasonStatsCommand { Season = _options.LiveSeasonYear.ToString(), Force = true };

                statsUpserted = await _mediator.Send(command, cancellationToken);
                statExecuted = true;

                _logger.LogInformation("Scheduled season {Season} stat sync completed: {Count} records upserted.", _options.LiveSeasonYear, statsUpserted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                hadError = true;
                _logger.LogError(ex, "Scheduled season {Season} stat sync failed.", _options.LiveSeasonYear);
            }
        }

        TimeSpan nextDelay = hadError ? TimeSpan.FromMinutes(10) : plan.NextDelayIfClean;
        return new LiveSyncCycleResult(injuryExecuted, injuryMatched, statExecuted, statsUpserted, nextDelay);
    }
}
