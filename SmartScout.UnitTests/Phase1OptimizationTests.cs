using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Caching.Memory;
using SmartScout.Application.Common.Queries;
using SmartScout.Application.Interfaces;
using SmartScout.Application.Services;
using SmartScout.Application.UseCases.Analytics;
using SmartScout.Application.UseCases.Players;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Data;
using Xunit;

namespace SmartScout.UnitTests;

public class Phase1OptimizationTests
{
    private static SmartScoutDbContext CreateNpgsqlModelContext()
    {
        var options = new DbContextOptionsBuilder<SmartScoutDbContext>()
            .UseNpgsql("Host=localhost;Database=SmartScoutDb;Username=postgres;Password=dummy")
            .Options;

        return new SmartScoutDbContext(options);
    }

    [Fact]
    public void AnalyticsSchemaIndexes_AreConfiguredInDbContextModel()
    {
        using var db = CreateNpgsqlModelContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;

        var playerEntity = designModel.FindEntityType(typeof(Player))!;
        var teamTrigramIndex = playerEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Player.TeamName) }));
        Assert.NotNull(teamTrigramIndex);
        Assert.Equal("gin", teamTrigramIndex!.GetMethod());
        Assert.Equal(new[] { "gin_trgm_ops" }, teamTrigramIndex.GetOperators());

        var injuryEntity = designModel.FindEntityType(typeof(PlayerInjury))!;
        var injuryStatusIndex = injuryEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(PlayerInjury.Status) }));
        Assert.NotNull(injuryStatusIndex);

        var statEntity = designModel.FindEntityType(typeof(PlayerSeasonStat))!;
        var statSeasonLeagueIndex = statEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(PlayerSeasonStat.Season), nameof(PlayerSeasonStat.League) }));
        Assert.NotNull(statSeasonLeagueIndex);
    }

    [Fact]
    public void FromAnalyticsProjection_MapsAllAnalyticsFieldsAndExcludesHeavyJsonFields()
    {
        var player = new Player(
            externalApiId: 9001,
            firstName: "Nikola",
            lastName: "Jokić",
            position: "C-F",
            height: "6-11",
            weightPounds: 284,
            teamName: "DEN",
            league: "NBA",
            nationality: "SRB",
            birthDate: new DateTime(1995, 2, 19, 0, 0, 0, DateTimeKind.Utc),
            heightCm: 211,
            weightKg: 129,
            wingspanCm: 221,
            externalIdsJson: "{\"nba\":203999}");
        player.SetScoutNotes("Confidential note");
        player.UpdateScoutingIntel(standingReachCm: 282, contractExpirySeason: 2028, estimatedMarketValueTier: "Tier 1 • Franchise", agencyName: "BeoBasket", dominantHand: "Right");

        var stat = new PlayerSeasonStat(
            playerId: player.Id,
            season: 2027,
            gamesPlayed: 60,
            minutes: 34.5m,
            points: 27.1m,
            assists: 9.8m,
            rebounds: 12.4m,
            steals: 1.5m,
            blocks: 0.9m,
            fgPct: 0.58m,
            fg3Pct: 0.36m,
            ftPct: 0.82m,
            turnovers: 3.1m,
            trueShootingPct: 0.65m,
            playerEfficiencyRating: 31.2m,
            usagePct: 30.1m,
            league: "NBA",
            teamName: "DEN",
            rawScrapedPayload: "{\"heavy\":\"json\"}");

        var projectedPlayer = Player.FromAnalyticsProjection(
            player.Id, player.FirstName, player.LastName, player.FullName, player.NormalizedName,
            player.Position, player.PrimaryPosition, player.SecondaryPosition,
            player.Height, player.HeightCm, player.WeightPounds, player.WeightKg,
            player.WingspanCm, player.StandingReachCm, player.TeamName, player.League,
            player.Nationality, player.BirthDate, player.ContractExpirySeason,
            player.EstimatedMarketValueTier, player.AgencyName, player.DominantHand, player.IsWatchlisted);

        var projectedStat = PlayerSeasonStat.FromAnalyticsProjection(
            stat.Id, stat.PlayerId, stat.Season, stat.League, stat.TeamName, stat.CompetitionPhase,
            stat.LeagueDifficultyCoefficient, stat.GamesPlayed, stat.GamesStarted, stat.MinutesPlayedTotal, stat.Minutes,
            stat.Points, stat.FieldGoalsMade, stat.FieldGoalsAttempted, stat.FgPct,
            stat.ThreePointsMade, stat.ThreePointsAttempted, stat.Fg3Pct,
            stat.TwoPointsMade, stat.TwoPointsAttempted, stat.Fg2Pct,
            stat.FreeThrowsMade, stat.FreeThrowsAttempted, stat.FtPct,
            stat.OffensiveRebounds, stat.DefensiveRebounds, stat.Rebounds,
            stat.Assists, stat.Steals, stat.Blocks, stat.Turnovers,
            stat.PersonalFoulsCommitted, stat.FoulsDrawn, stat.PlusMinus,
            stat.PtsPer36, stat.RebPer36, stat.AstPer36, stat.StlPer36, stat.BlkPer36, stat.TovPer36,
            stat.PointsPer40, stat.AssistsPer40, stat.ReboundsPer40,
            stat.TrueShootingPct, stat.EffectiveFgPct, stat.UsagePct, stat.AssistToTurnoverRatio, stat.GameScoreAverage,
            stat.Pir, stat.PlayerEfficiencyRating, stat.TotalReboundPct, stat.AssistPct, stat.WinShares, stat.BoxPlusMinus,
            stat.StocksPer36, stat.ThreePointRate, stat.EuroLeagueTransitionIndex);

        projectedStat.AttachPlayer(projectedPlayer);

        Assert.Null(projectedPlayer.ExternalIdsJson);
        Assert.Null(projectedPlayer.ScoutNotes);
        Assert.Null(projectedStat.RawScrapedPayload);
        Assert.Equal("Nikola Jokić", projectedPlayer.FullName);
        Assert.Equal("nikola jokic", projectedPlayer.NormalizedName);
        Assert.Equal(282, projectedPlayer.StandingReachCm);
        Assert.Equal(stat.EuroLeagueTransitionIndex, projectedStat.EuroLeagueTransitionIndex);
        Assert.Equal(stat.PtsPer36, projectedStat.PtsPer36);
        Assert.Same(projectedPlayer, projectedStat.Player);
    }

    [Fact]
    public void SeasonStatAnalyticsLoader_ProjectQuerySql_OmitsRawScrapedPayloadExternalIdsAndScoutNotes()
    {
        using var db = CreateNpgsqlModelContext();
        string sql = SeasonStatAnalyticsLoader.BuildAnalyticsQuery(db.SeasonStats.AsNoTracking()).ToQueryString();

        Assert.DoesNotContain("RawScrapedPayload", sql);
        Assert.DoesNotContain("ExternalIdsJson", sql);
        Assert.DoesNotContain("ScoutNotes", sql);
        Assert.Contains("EuroLeagueTransitionIndex", sql);
    }

    [Fact]
    public async Task SeasonStatAnalyticsLoader_LoadProjectedStats_DeduplicatesPlayersAndOmitsRawJson()
    {
        var p1 = new Player(101, "Alex", "Guard", "PG", "6-4", 195, "Fener", "EuroLeague", "TUR",
            new DateTime(2000, 5, 1, 0, 0, 0, DateTimeKind.Utc), 193, 88, externalIdsJson: "{\"id\":1}");
        p1.SetScoutNotes("Sensitive notes");

        var s2026 = new PlayerSeasonStat(p1.Id, 2026, 28, 25.0m, 14.0m, 5.5m, 3.2m, 1.3m, 0.2m, 0.48m, 0.39m, 0.85m, 2.0m,
            league: "EuroLeague", teamName: "Fener", rawScrapedPayload: "{\"raw\":1}");
        var s2027 = new PlayerSeasonStat(p1.Id, 2027, 30, 27.0m, 15.8m, 6.2m, 3.6m, 1.5m, 0.3m, 0.50m, 0.41m, 0.88m, 1.9m,
            league: "EuroLeague", teamName: "Fener", rawScrapedPayload: "{\"raw\":2}");

        AttachPlayerToStat(s2026, p1);
        AttachPlayerToStat(s2027, p1);

        var db = new FakeSmartScoutDbContext(new[] { p1 }, new[] { s2026, s2027 });

        var loaded = await SeasonStatAnalyticsLoader.LoadProjectedStatsAsync(db, linkCareerHistory: true, cancellationToken: CancellationToken.None);

        Assert.Equal(2, loaded.Count);
        Assert.All(loaded, s => Assert.Null(s.RawScrapedPayload));
        Assert.Same(loaded[0].Player, loaded[1].Player);
        Assert.Null(loaded[0].Player!.ExternalIdsJson);
        Assert.Null(loaded[0].Player!.ScoutNotes);
        Assert.Equal(2, loaded[0].Player!.SeasonStats.Count);
    }

    [Fact]
    public async Task SmartScoutSearchQueryHandler_InvalidatesCache_WhenCacheVersionBumps()
    {
        var (db, pA, _, _) = BuildSampleAnalyticsDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheState = new ScoutingCacheState();
        var engine = new SmartScoutSearchEngine(new PlayerAnalyticsService());
        var handler = new SmartScoutSearchQueryHandler(db, engine, cache, cacheState);

        var first = await handler.Handle(new SmartScoutSearchQuery { Prompt = "euroleague", Season = 2027, Top = 10 }, CancellationToken.None);
        Assert.NotEmpty(first);

        pA.UpdateDetails("Renamed", pA.LastName, pA.Position, pA.Height, pA.WeightPounds, pA.TeamName, pA.League, pA.Nationality, pA.BirthDate);

        var cachedStillOld = await handler.Handle(new SmartScoutSearchQuery { Prompt = "euroleague", Season = 2027, Top = 10 }, CancellationToken.None);
        Assert.Contains(cachedStillOld, r => r.FullName.StartsWith("Elena"));

        cacheState.BumpVersion();

        var refreshed = await handler.Handle(new SmartScoutSearchQuery { Prompt = "euroleague", Season = 2027, Top = 10 }, CancellationToken.None);
        Assert.Contains(refreshed, r => r.FullName.StartsWith("Renamed"));
    }

    [Fact]
    public async Task GetSimilarPlayersQueryHandler_InvalidatesCache_WhenCacheVersionBumps()
    {
        var (db, pA, pB, _) = BuildSampleAnalyticsDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheState = new ScoutingCacheState();
        var handler = new GetSimilarPlayersQueryHandler(db, new PlayerAnalyticsService(), cache, cacheState);

        var query = new GetSimilarPlayersQuery { PlayerId = pA.Id, Season = 2027, Top = 5, MinGamesPlayed = 5 };
        var first = await handler.Handle(query, CancellationToken.None);
        Assert.Single(first);

        pB.UpdateDetails("Updated", pB.LastName, pB.Position, pB.Height, pB.WeightPounds, pB.TeamName, pB.League, pB.Nationality, pB.BirthDate);

        var cachedStillOld = await handler.Handle(query, CancellationToken.None);
        Assert.Equal("Marco", cachedStillOld[0].FirstName);

        cacheState.BumpVersion();

        var refreshed = await handler.Handle(query, CancellationToken.None);
        Assert.Equal("Updated", refreshed[0].FirstName);
    }

    [Fact]
    public async Task ComparePlayersQueryHandler_InvalidatesCache_WhenCacheVersionBumps()
    {
        var (db, pA, pB, _) = BuildSampleAnalyticsDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheState = new ScoutingCacheState();
        var handler = new ComparePlayersQueryHandler(db, new PlayerAnalyticsService(), cache, cacheState);

        var query = new ComparePlayersQuery { PlayerAId = pA.Id, SeasonA = 2027, PlayerBId = pB.Id, SeasonB = 2027 };
        var first = await handler.Handle(query, CancellationToken.None);
        Assert.Equal("Elena Prospect", first.PlayerA.FullName);

        pA.UpdateDetails("ElenaUpdated", pA.LastName, pA.Position, pA.Height, pA.WeightPounds, pA.TeamName, pA.League, pA.Nationality, pA.BirthDate);

        var cachedStillOld = await handler.Handle(query, CancellationToken.None);
        Assert.Equal("Elena Prospect", cachedStillOld.PlayerA.FullName);

        cacheState.BumpVersion();

        var refreshed = await handler.Handle(query, CancellationToken.None);
        Assert.Equal("ElenaUpdated Prospect", refreshed.PlayerA.FullName);
    }

    [Fact]
    public async Task SyncPlayersCommandHandler_BumpsCacheVersion_OnlyWhenPlayersUpserted()
    {
        var cacheState = new ScoutingCacheState();
        long initialVersion = cacheState.Version;

        var emptyDb = new FakeSmartScoutDbContext(Array.Empty<Player>(), Array.Empty<PlayerSeasonStat>());
        var emptyHandler = new SyncPlayersCommandHandler(new[] { new StubPlayerSyncStrategy(Array.Empty<Player>()) }, emptyDb, cacheState);
        int zeroCount = await emptyHandler.Handle(new SyncPlayersCommand(), CancellationToken.None);
        Assert.Equal(0, zeroCount);
        Assert.Equal(initialVersion, cacheState.Version);

        var newPlayer = new Player(5050, "Test", "Guard", "SG", "6-5", 200, "Real Madrid", "ACB");
        var populatedHandler = new SyncPlayersCommandHandler(new[] { new StubPlayerSyncStrategy(new[] { newPlayer }) }, emptyDb, cacheState);
        int syncedCount = await populatedHandler.Handle(new SyncPlayersCommand(), CancellationToken.None);
        Assert.Equal(1, syncedCount);
        Assert.Equal(initialVersion + 1, cacheState.Version);
    }

    [Theory]
    [InlineData("jokic", "jokic")]
    [InlineData("100%_real\\name", "100\\%\\_real\\\\name")]
    public void GetPlayersQueryHandler_EscapeLikePattern_EscapesSpecialWildcards(string input, string expected)
    {
        Assert.Equal(expected, GetPlayersQueryHandler.EscapeLikePattern(input));
    }

    [Fact]
    public void GetPlayersQueryHandler_BuildSearchQuerySql_UsesILikeAndNormalizedName()
    {
        using var db = CreateNpgsqlModelContext();
        string sql = GetPlayersQueryHandler.ApplySearchFilter(db.Players.AsNoTracking(), "Nikola Jokić").ToQueryString();

        Assert.Contains("ILIKE", sql);
        Assert.Contains("NormalizedName", sql);
        Assert.Contains("TeamName", sql);
        Assert.DoesNotContain("lower(", sql);
    }

    private static (FakeSmartScoutDbContext Db, Player A, Player B, PlayerSeasonStat StatA) BuildSampleAnalyticsDb()
    {
        var pA = new Player(1001, "Elena", "Prospect", "PG", "6-3", 185, "Fenerbahçe", "EuroLeague", "TUR",
            new DateTime(2002, 4, 10, 0, 0, 0, DateTimeKind.Utc), 191, 84);
        var pB = new Player(1002, "Marco", "Guard", "PG", "6-4", 190, "Virtus", "EuroLeague", "ITA",
            new DateTime(2001, 6, 12, 0, 0, 0, DateTimeKind.Utc), 193, 86);

        var sA = new PlayerSeasonStat(pA.Id, 2027, 24, 28m, 16.5m, 6.4m, 3.8m, 1.5m, 0.2m, 0.49m, 0.40m, 0.87m, 2.1m,
            league: "EuroLeague", teamName: "Fenerbahçe");
        var sB = new PlayerSeasonStat(pB.Id, 2027, 25, 26m, 14.8m, 5.9m, 3.5m, 1.3m, 0.2m, 0.47m, 0.38m, 0.84m, 1.9m,
            league: "EuroLeague", teamName: "Virtus");

        AttachPlayerToStat(sA, pA);
        AttachPlayerToStat(sB, pB);

        var db = new FakeSmartScoutDbContext(new[] { pA, pB }, new[] { sA, sB });
        return (db, pA, pB, sA);
    }

    private static void AttachPlayerToStat(PlayerSeasonStat stat, Player player)
    {
        stat.AttachPlayer(player);
        player.AddSeasonStat(stat);
    }

    private sealed class StubPlayerSyncStrategy : IPlayerSyncStrategy
    {
        private readonly IReadOnlyList<Player> _players;
        public StubPlayerSyncStrategy(IReadOnlyList<Player> players) => _players = players;

        public async IAsyncEnumerable<IEnumerable<Player>> FetchPlayersAsync(
            int cursor,
            [EnumeratorCancellation] CancellationToken cancellationToken,
            SmartScout.Application.DTOs.SyncFilterContext? filter = null)
        {
            if (_players.Count > 0)
                yield return _players;
            await Task.CompletedTask;
        }
    }

    private sealed class FakeSmartScoutDbContext : ISmartScoutDbContext
    {
        public DbSet<Player> Players { get; }
        public DbSet<PlayerSeasonStat> SeasonStats { get; }
        public DbSet<PlayerInjury> PlayerInjuries { get; }
        public DbSet<BasketballLeague> Leagues { get; }
        public DbSet<BasketballSeason> Seasons { get; }
        public DbSet<IngestionJobLog> IngestionJobLogs { get; }
        public DbSet<ScoutBoard> ScoutBoards { get; }
        public DbSet<ScoutBoardEntry> ScoutBoardEntries { get; }

        public FakeSmartScoutDbContext(IEnumerable<Player> players, IEnumerable<PlayerSeasonStat> stats)
        {
            Players = new InMemoryAsyncDbSet<Player>(players);
            SeasonStats = new InMemoryAsyncDbSet<PlayerSeasonStat>(stats);
            PlayerInjuries = new InMemoryAsyncDbSet<PlayerInjury>(Array.Empty<PlayerInjury>());
            Leagues = new InMemoryAsyncDbSet<BasketballLeague>(Array.Empty<BasketballLeague>());
            Seasons = new InMemoryAsyncDbSet<BasketballSeason>(Array.Empty<BasketballSeason>());
            IngestionJobLogs = new InMemoryAsyncDbSet<IngestionJobLog>(Array.Empty<IngestionJobLog>());
            ScoutBoards = new InMemoryAsyncDbSet<ScoutBoard>(Array.Empty<ScoutBoard>());
            ScoutBoardEntries = new InMemoryAsyncDbSet<ScoutBoardEntry>(Array.Empty<ScoutBoardEntry>());
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class InMemoryAsyncDbSet<T> : DbSet<T>, IQueryable<T>, IAsyncEnumerable<T> where T : class
    {
        private readonly List<T> _data;
        private readonly IQueryable<T> _query;

        public InMemoryAsyncDbSet(IEnumerable<T> items)
        {
            _data = items.ToList();
            _query = _data.AsQueryable();
        }

        public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType => null!;
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Add(T entity) { _data.Add(entity); return null!; }
        public override ValueTask<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>> AddAsync(T entity, CancellationToken cancellationToken = default) { _data.Add(entity); return ValueTask.FromResult<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>>(null!); }
        public override void RemoveRange(IEnumerable<T> entities) { foreach (var e in entities.ToList()) _data.Remove(e); }

        Type IQueryable.ElementType => _query.ElementType;
        Expression IQueryable.Expression => _query.Expression;
        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(_query.Provider);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();

        public override IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new TestAsyncEnumerator<T>(_data.GetEnumerator());
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;
        public TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression);
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

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(Expression expression) : base(expression) { }
        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;
        public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;
        public T Current => _inner.Current;
        public ValueTask DisposeAsync() { _inner.Dispose(); return ValueTask.CompletedTask; }
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());
    }
}
