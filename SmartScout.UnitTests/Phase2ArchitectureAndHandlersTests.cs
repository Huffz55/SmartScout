using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using SmartScout.Api.BackgroundServices;
using SmartScout.Api.Controllers;
using SmartScout.Application.Interfaces;
using SmartScout.Application.Services;
using SmartScout.Application.UseCases.Boards;
using SmartScout.Application.UseCases.Injuries;
using SmartScout.Application.UseCases.Players;
using SmartScout.Application.UseCases.Teams;
using SmartScout.Domain.Entities;
using Xunit;

namespace SmartScout.UnitTests;

public class Phase2ArchitectureAndHandlersTests
{
    [Fact]
    public void ArchitectureBoundary_ApplicationHasNoNpgsql_AndControllersHaveNoInfrastructureDependencies()
    {
        var applicationRefs = typeof(GetPlayersQueryHandler).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(applicationRefs, r => r.Name != null && r.Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase));

        var apiTypesToCheck = new[]
        {
            typeof(PlayersController),
            typeof(BoardsController),
            typeof(SeasonStatSyncBackgroundService)
        };

        foreach (var type in apiTypesToCheck)
        {
            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    string ns = param.ParameterType.Namespace ?? string.Empty;
                    Assert.DoesNotContain("SmartScout.Infrastructure", ns, StringComparison.Ordinal);
                    Assert.DoesNotContain("SmartScoutDbContext", param.ParameterType.Name, StringComparison.Ordinal);
                    Assert.NotEqual(typeof(ISmartScoutDbContext), param.ParameterType);
                }
            }

            foreach (var method in type.GetMethods().Where(m => m.DeclaringType == type))
            {
                foreach (var param in method.GetParameters())
                {
                    string ns = param.ParameterType.Namespace ?? string.Empty;
                    Assert.DoesNotContain("SmartScout.Infrastructure", ns, StringComparison.Ordinal);
                    Assert.NotEqual(typeof(ISmartScoutDbContext), param.ParameterType);
                }
            }
        }

        Assert.All(new[] { typeof(PlayersController), typeof(BoardsController) }, controllerType =>
        {
            var ctor = Assert.Single(controllerType.GetConstructors());
            var param = Assert.Single(ctor.GetParameters());
            Assert.Equal(typeof(IMediator), param.ParameterType);
        });
    }

    [Fact]
    public void CreateScoutBoardAndSaveNotesValidators_RejectInvalidInputs()
    {
        var createValidator = new CreateScoutBoardCommandValidator();
        Assert.False(createValidator.Validate(new CreateScoutBoardCommand(string.Empty)).IsValid);
        Assert.False(createValidator.Validate(new CreateScoutBoardCommand(new string('A', 101))).IsValid);
        Assert.False(createValidator.Validate(new CreateScoutBoardCommand("Summer Guards", new string('B', 81))).IsValid);
        Assert.True(createValidator.Validate(new CreateScoutBoardCommand("Summer Guards", "Lead Scout")).IsValid);

        var notesValidator = new SaveBoardPlayerNotesCommandValidator();
        Assert.False(notesValidator.Validate(new SaveBoardPlayerNotesCommand(Guid.NewGuid(), Guid.NewGuid(), new string('X', 2001))).IsValid);
        Assert.True(notesValidator.Validate(new SaveBoardPlayerNotesCommand(Guid.NewGuid(), Guid.NewGuid(), "Solid P&R defender")).IsValid);
    }

    [Fact]
    public async Task BoardsHandlers_CreateListToggleNoteAndDelete_PersistAndThrowWhenMissing()
    {
        var p1 = new Player(11, "Shane", "Larkin", "PG", "5-11", 175, "Anadolu Efes", "EuroLeague", "TUR",
            new DateTime(1992, 10, 2, 0, 0, 0, DateTimeKind.Utc), 180, 79);
        var db = new Phase2FakeDbContext(new[] { p1 }, Array.Empty<PlayerSeasonStat>(), Array.Empty<PlayerInjury>(), Array.Empty<IngestionJobLog>());

        var createHandler = new CreateScoutBoardCommandHandler(db);
        var created = await createHandler.Handle(new CreateScoutBoardCommand("EuroLeague Guards", "Halim"), CancellationToken.None);
        Assert.Equal("EuroLeague Guards", created.Name);

        var listHandler = new GetScoutBoardsQueryHandler(db);
        var boards = await listHandler.Handle(new GetScoutBoardsQuery(), CancellationToken.None);
        Assert.Single(boards);

        var toggleHandler = new TogglePlayerOnBoardCommandHandler(db);
        var toggled = await toggleHandler.Handle(new TogglePlayerOnBoardCommand(created.Id, p1.Id), CancellationToken.None);
        Assert.True(toggled.IsWatchlisted);

        var notesHandler = new SaveBoardPlayerNotesCommandHandler(db);
        var savedNote = await notesHandler.Handle(new SaveBoardPlayerNotesCommand(created.Id, p1.Id, "Priority 2027"), CancellationToken.None);
        Assert.Equal("Priority 2027", savedNote.ScoutNotes);

        var playersHandler = new GetBoardPlayersQueryHandler(db);
        var boardPlayers = await playersHandler.Handle(new GetBoardPlayersQuery(created.Id), CancellationToken.None);
        var bp = Assert.Single(boardPlayers);
        Assert.Equal("Priority 2027", bp.ScoutNotes);

        var deleteHandler = new DeleteScoutBoardCommandHandler(db);
        var deleted = await deleteHandler.Handle(new DeleteScoutBoardCommand(created.Id), CancellationToken.None);
        Assert.True(deleted.Deleted);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            deleteHandler.Handle(new DeleteScoutBoardCommand(created.Id), CancellationToken.None));
    }

    [Fact]
    public async Task TeamsHandlersAndValidator_GroupSummarizeAndResolveNbaAliases()
    {
        var validator = new GetTeamRosterQueryValidator();
        Assert.False(validator.Validate(new GetTeamRosterQuery("NBA", string.Empty)).IsValid);
        Assert.True(validator.Validate(new GetTeamRosterQuery("NBA", "DAL")).IsValid);

        var p1 = new Player(21, "Cooper", "Flagg", "SF", "6-9", 205, "DAL", "NBA", "USA",
            new DateTime(2006, 12, 21, 0, 0, 0, DateTimeKind.Utc), 206, 93);
        var p2 = new Player(22, "Dereck", "Lively", "C", "7-1", 230, "DAL", "NBA", "USA",
            new DateTime(2004, 2, 12, 0, 0, 0, DateTimeKind.Utc), 216, 104);

        var s1 = new PlayerSeasonStat(p1.Id, 2027, 25, 32m, 22.4m, 5.1m, 7.2m, 1.4m, 1.3m, 0.50m, 0.37m, 0.81m, 2.2m, league: "NBA", teamName: "DAL");
        var s2 = new PlayerSeasonStat(p2.Id, 2026, 40, 26m, 10.5m, 1.8m, 8.2m, 0.7m, 1.8m, 0.68m, null, 0.65m, 1.2m, league: "NBA", teamName: "DAL");
        Attach(s1, p1);
        Attach(s2, p2);

        var inj = new PlayerInjury(p1.Id, 2027, "Ankle", "Day-To-Day", DateTime.UtcNow, source: "ESPN");
        var db = new Phase2FakeDbContext(new[] { p1, p2 }, new[] { s1, s2 }, new[] { inj }, Array.Empty<IngestionJobLog>());

        var summaryHandler = new GetTeamsSummaryQueryHandler(db);
        var summary = await summaryHandler.Handle(new GetTeamsSummaryQuery("NBA", 2027, "DAL", 10), CancellationToken.None);
        var dal = Assert.Single(summary);
        Assert.Equal("DAL", dal.TeamName);
        Assert.Equal(1, dal.ActiveInjuriesCount);

        var rosterHandler = new GetTeamRosterQueryHandler(db);
        var roster = await rosterHandler.Handle(new GetTeamRosterQuery("NBA", "Dallas Mavericks", 2027), CancellationToken.None);
        Assert.Equal(1, roster.PlayerCount);
        Assert.Equal(1, roster.ActiveInjuriesCount);
        Assert.Equal("Cooper Flagg", roster.Roster[0].FullName);
        Assert.Equal("Day-To-Day", roster.Roster[0].ActiveInjuryStatus);
    }

    [Fact]
    public async Task InjuriesAndSyncStatusHandlers_ReturnExpectedCountsAndPersistCheckpoints()
    {
        var p1 = new Player(31, "Mathias", "Lessort", "C", "6-9", 247, "Panathinaikos", "EuroLeague", "FRA");
        var injActive = new PlayerInjury(p1.Id, 2027, "Knee", "Out", DateTime.UtcNow, source: "Eurohoops");
        var injRecovered = new PlayerInjury(p1.Id, 2027, "Ankle", "Recovered", DateTime.UtcNow.AddDays(-7), source: "Eurohoops");
        AttachInjury(injActive, p1);
        AttachInjury(injRecovered, p1);

        var log = new IngestionJobLog("EuroLeague", 2027, "Completed", 92);
        var db = new Phase2FakeDbContext(new[] { p1 }, Array.Empty<PlayerSeasonStat>(), new[] { injActive, injRecovered }, new[] { log });
        var settings = new FakeRuntimeSettings();

        var injSummaryHandler = new GetInjuriesSummaryQueryHandler(db);
        var injSummary = await injSummaryHandler.Handle(new GetInjuriesSummaryQuery(ActiveOnly: true, Top: 20), CancellationToken.None);
        Assert.Equal(2, injSummary.TotalRecords);
        Assert.Equal(1, injSummary.ActiveRecords);
        Assert.Single(injSummary.Items);

        var syncInjHandler = new SyncLiveInjuriesCommandHandler(new StubInjurySyncService(), db, settings);
        var syncResp = await syncInjHandler.Handle(new SyncLiveInjuriesCommand(), CancellationToken.None);
        Assert.Equal(4, syncResp.MatchedPlayers);
        Assert.Contains(db.IngestionJobLogs, l => l.LeagueCode == SyncCheckpointConstants.InjuryCheckpointCode && l.Status == "Completed");

        var statusHandler = new GetSyncStatusQueryHandler(db, settings);
        var status = await statusHandler.Handle(new GetSyncStatusQuery(), CancellationToken.None);
        Assert.Equal(2027, status.LiveSeasonYear);
        Assert.Equal(1, status.ActiveInjuriesCount);
        Assert.Equal(1, status.TotalPlayersCount);
    }

    [Fact]
    public async Task SeedDemoWatchlistCommandHandler_SelectsDiversePlayersAndBumpsCacheVersion()
    {
        var g = new Player(41, "Guard", "One", "PG", "6-3", 190, "TeamA", "EuroLeague", "ESP", new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var f = new Player(42, "Forward", "Two", "SF", "6-8", 220, "TeamB", "ACB", "FRA", new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var c = new Player(43, "Center", "Three", "C", "7-0", 245, "TeamC", "BSL", "TUR", new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var sg = new PlayerSeasonStat(g.Id, 2027, 20, 28m, 15m, 6m, 3m, 1.5m, 0.2m, 0.48m, 0.39m, 0.85m, 2m, league: "EuroLeague", teamName: "TeamA");
        var sf = new PlayerSeasonStat(f.Id, 2027, 20, 28m, 14m, 3m, 6m, 1.3m, 0.7m, 0.50m, 0.38m, 0.82m, 1.5m, league: "ACB", teamName: "TeamB");
        var sc = new PlayerSeasonStat(c.Id, 2027, 20, 26m, 13m, 2m, 9m, 0.8m, 1.9m, 0.62m, null, 0.72m, 1.4m, league: "BSL", teamName: "TeamC");
        Attach(sg, g);
        Attach(sf, f);
        Attach(sc, c);

        var db = new Phase2FakeDbContext(new[] { g, f, c }, new[] { sg, sf, sc }, Array.Empty<PlayerInjury>(), Array.Empty<IngestionJobLog>());
        var cacheState = new ScoutingCacheState();
        long vBefore = cacheState.Version;

        var handler = new SeedDemoWatchlistCommandHandler(db, new FakeRuntimeSettings(), cacheState);
        var result = await handler.Handle(new SeedDemoWatchlistCommand(), CancellationToken.None);

        Assert.Equal(3, result.AddedCount);
        Assert.Equal(3, result.TotalWatchlisted);
        Assert.Equal(vBefore + 1, cacheState.Version);
    }

    private static void Attach(PlayerSeasonStat stat, Player player)
    {
        stat.AttachPlayer(player);
        player.AddSeasonStat(stat);
    }

    private static void AttachInjury(PlayerInjury inj, Player player)
    {
        var prop = typeof(PlayerInjury).GetProperty(nameof(PlayerInjury.Player));
        prop?.SetValue(inj, player);
        player.AddInjury(inj);
    }

    private sealed class FakeRuntimeSettings : IScraperRuntimeSettings
    {
        public bool EnableBackgroundSync => true;
        public bool EnableLiveInjuryAutoSync => true;
        public int InjurySyncIntervalHours => 4;
        public int CurrentSeasonStatRefreshHours => 24;
        public int LiveSeasonYear => 2027;
        public int StartupSyncDelaySeconds => 20;
    }

    private sealed class StubInjurySyncService : IPlayerInjurySyncService
    {
        public Task<InjurySyncSummaryDto> SyncLiveInjuriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new InjurySyncSummaryDto(
                10,
                4,
                2,
                2,
                0,
                new Dictionary<string, int> { ["ESPN"] = 4 },
                new List<SyncedInjuryEntryDto>
                {
                    new("Cooper Flagg", "NBA", "Ankle", "Day-To-Day", "ESPN", null)
                }));
    }

    private sealed class Phase2FakeDbContext : ISmartScoutDbContext
    {
        public DbSet<Player> Players { get; }
        public DbSet<PlayerSeasonStat> SeasonStats { get; }
        public DbSet<PlayerInjury> PlayerInjuries { get; }
        public DbSet<BasketballLeague> Leagues { get; }
        public DbSet<BasketballSeason> Seasons { get; }
        public DbSet<IngestionJobLog> IngestionJobLogs { get; }
        public DbSet<ScoutBoard> ScoutBoards { get; }
        public DbSet<ScoutBoardEntry> ScoutBoardEntries { get; }

        public Phase2FakeDbContext(
            IEnumerable<Player> players,
            IEnumerable<PlayerSeasonStat> stats,
            IEnumerable<PlayerInjury> injuries,
            IEnumerable<IngestionJobLog> logs)
        {
            Players = new AsyncListDbSet<Player>(players);
            SeasonStats = new AsyncListDbSet<PlayerSeasonStat>(stats);
            PlayerInjuries = new AsyncListDbSet<PlayerInjury>(injuries);
            Leagues = new AsyncListDbSet<BasketballLeague>(Array.Empty<BasketballLeague>());
            Seasons = new AsyncListDbSet<BasketballSeason>(Array.Empty<BasketballSeason>());
            IngestionJobLogs = new AsyncListDbSet<IngestionJobLog>(logs);
            ScoutBoards = new AsyncListDbSet<ScoutBoard>(Array.Empty<ScoutBoard>());
            ScoutBoardEntries = new AsyncListDbSet<ScoutBoardEntry>(Array.Empty<ScoutBoardEntry>());
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ((IEnumerable<ScoutBoardEntry>)ScoutBoardEntries).ToList())
            {
                var p = ((IEnumerable<Player>)Players).FirstOrDefault(x => x.Id == entry.PlayerId);
                if (p != null)
                {
                    typeof(ScoutBoardEntry).GetProperty(nameof(ScoutBoardEntry.Player))?.SetValue(entry, p);
                }
            }
            return Task.FromResult(1);
        }
    }

    private sealed class AsyncListDbSet<T> : DbSet<T>, IQueryable<T>, IAsyncEnumerable<T> where T : class
    {
        private readonly List<T> _data;
        private readonly IQueryable<T> _query;

        public AsyncListDbSet(IEnumerable<T> items)
        {
            _data = items.ToList();
            _query = _data.AsQueryable();
        }

        public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType => null!;
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Add(T entity) { _data.Add(entity); return null!; }
        public override ValueTask<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>> AddAsync(T entity, CancellationToken cancellationToken = default) { _data.Add(entity); return ValueTask.FromResult<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>>(null!); }
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Remove(T entity) { _data.Remove(entity); return null!; }
        public override void RemoveRange(IEnumerable<T> entities) { foreach (var e in entities.ToList()) _data.Remove(e); }

        Type IQueryable.ElementType => _query.ElementType;
        Expression IQueryable.Expression => _query.Expression;
        IQueryProvider IQueryable.Provider => new AsyncQueryProvider<T>(_query.Provider);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();

        public override IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new AsyncEnumerator<T>(_data.GetEnumerator());
    }

    private sealed class AsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;
        public AsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) => new AsyncEnumerable<TEntity>(expression);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new AsyncEnumerable<TElement>(expression);
        public object? Execute(Expression expression) => _inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executed = typeof(IQueryProvider)
                .GetMethods()
                .First(m => m.Name == nameof(IQueryProvider.Execute) && m.IsGenericMethod)
                .MakeGenericMethod(resultType)
                .Invoke(_inner, new object[] { expression });

            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { executed })!;
        }
    }

    private sealed class AsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public AsyncEnumerable(Expression expression) : base(expression) { }
        IQueryProvider IQueryable.Provider => new AsyncQueryProvider<T>(this);
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new AsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    private sealed class AsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;
        public AsyncEnumerator(IEnumerator<T> inner) => _inner = inner;
        public T Current => _inner.Current;
        public ValueTask DisposeAsync() { _inner.Dispose(); return ValueTask.CompletedTask; }
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());
    }
}
