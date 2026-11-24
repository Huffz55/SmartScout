using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SmartScout.Api.Attributes;
using SmartScout.Application.UseCases.Boards;
using SmartScout.Application.UseCases.Players;
using SmartScout.Application.UseCases.Teams;
using SmartScout.Domain.Entities;
using SmartScout.Infrastructure.Data;

namespace SmartScout.IntegrationTests;

[Collection("PostgresIntegration")]
public sealed class SmartScoutPostgresIntegrationTests : IAsyncLifetime
{
    private readonly SmartScoutWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartScoutPostgresIntegrationTests(SmartScoutWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record PlayerDirectoryItemDto(Guid Id, string FullName, string League, string? TeamName);

    private sealed record PagedPlayersResponseDto(
        List<PlayerDirectoryItemDto> Items,
        int Page,
        int PageSize,
        int TotalCount);

    [Fact]
    public async Task PlayersSearch_ShouldMatchAccentInsensitiveAndPartialName_ViaPostgresILikeAndPgTrgm()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartScoutDbContext>();
            db.Players.AddRange(
                new Player(1001, "Nikola", "Jokić", "C", "6-11", 284, "Denver Nuggets", "NBA", "SRB",
                    new DateTime(1995, 2, 19, 0, 0, 0, DateTimeKind.Utc), 211, 129),
                new Player(1002, "Alperen", "Şengün", "C", "6-11", 243, "Houston Rockets", "NBA", "TUR",
                    new DateTime(2002, 7, 25, 0, 0, 0, DateTimeKind.Utc), 211, 110));
            await db.SaveChangesAsync();
        }

        var jokicMatch = await AssertSingleSearchMatchAsync("nikola%20jokic");
        Assert.Equal("Nikola Jokić", jokicMatch.FullName);

        var sengunMatch = await AssertSingleSearchMatchAsync("sengun");
        Assert.Equal("Alperen Şengün", sengunMatch.FullName);

        var teamMatch = await AssertSingleSearchMatchAsync("nuggets");
        Assert.Equal("Denver Nuggets", teamMatch.TeamName);
    }

    private async Task<PlayerDirectoryItemDto> AssertSingleSearchMatchAsync(string encodedQuery)
    {
        var response = await _client.GetAsync($"/api/players?search={encodedQuery}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedPlayersResponseDto>();
        Assert.NotNull(payload);
        return Assert.Single(payload.Items);
    }

    [Fact]
    public async Task BoardsCrud_ShouldCreateToggleAddNotesDelete_AndReturn400OnValidationFailure()
    {
        var player = new Player(2001, "Tarik", "Biberovic", "SF", "6-7", 218, "Fenerbahçe Beko", "EuroLeague", "BIH",
            new DateTime(2001, 1, 28, 0, 0, 0, DateTimeKind.Utc), 201, 99);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartScoutDbContext>();
            db.Players.Add(player);
            await db.SaveChangesAsync();
        }

        // Empty board name triggers FluentValidation -> 400 Bad Request
        var invalidResp = await _client.PostAsJsonAsync("/api/boards", new { name = "", ownerName = "Scout" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResp.StatusCode);

        // Create board
        var createResp = await _client.PostAsJsonAsync("/api/boards", new { name = "EuroLeague Targets", ownerName = "Lead Scout" });
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<ScoutBoardSummaryDto>();
        Assert.NotNull(created);
        Assert.Equal("EuroLeague Targets", created.Name);
        Assert.Equal("Lead Scout", created.OwnerName);

        // List boards
        var listResp = await _client.GetFromJsonAsync<List<ScoutBoardSummaryDto>>("/api/boards");
        Assert.NotNull(listResp);
        var listed = Assert.Single(listResp);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(0, listed.PlayerCount);

        // Toggle player onto board
        var toggleResp = await _client.PostAsync($"/api/boards/{created.Id}/players/{player.Id}", content: null);
        Assert.Equal(HttpStatusCode.OK, toggleResp.StatusCode);
        var toggled = await toggleResp.Content.ReadFromJsonAsync<BoardToggleResultDto>();
        Assert.NotNull(toggled);
        Assert.True(toggled.IsWatchlisted);

        // Save notes
        var notesResp = await _client.PostAsJsonAsync($"/api/boards/{created.Id}/players/{player.Id}/notes",
            new { notes = "Elite catch-and-shoot wing" });
        Assert.Equal(HttpStatusCode.OK, notesResp.StatusCode);

        // Verify persisted board player + notes
        var boardPlayers = await _client.GetFromJsonAsync<List<BoardPlayerDto>>($"/api/boards/{created.Id}/players");
        Assert.NotNull(boardPlayers);
        var bp = Assert.Single(boardPlayers);
        Assert.Equal(player.Id, bp.Id);
        Assert.Equal("Elite catch-and-shoot wing", bp.ScoutNotes);

        // Delete board
        var deleteResp = await _client.DeleteAsync($"/api/boards/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);
        var deleted = await deleteResp.Content.ReadFromJsonAsync<BoardDeleteResultDto>();
        Assert.NotNull(deleted);
        Assert.True(deleted.Deleted);

        var remaining = await _client.GetFromJsonAsync<List<ScoutBoardSummaryDto>>("/api/boards");
        Assert.NotNull(remaining);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task TeamRoster_ShouldReturnProjectedSeasonRoster_AndValidateMissingTeam()
    {
        var invalidResp = await _client.GetAsync("/api/players/teams/roster?team=");
        Assert.Equal(HttpStatusCode.BadRequest, invalidResp.StatusCode);

        var p1 = new Player(3001, "Nigel", "Hayes-Davis", "PF", "6-8", 254, "Fenerbahçe Beko", "EuroLeague", "USA",
            new DateTime(1994, 12, 16, 0, 0, 0, DateTimeKind.Utc), 203, 115);
        var p2 = new Player(3002, "Wade", "Baldwin", "PG", "6-4", 202, "Fenerbahçe Beko", "EuroLeague", "USA",
            new DateTime(1996, 3, 29, 0, 0, 0, DateTimeKind.Utc), 193, 92);

        var s1 = new PlayerSeasonStat(p1.Id, 2027, 30, 31.2m, 16.8m, 2.1m, 5.4m, 1.1m, 0.4m,
            0.49m, 0.41m, 0.88m, 1.4m, league: "EuroLeague", teamName: "Fenerbahçe Beko");
        s1.SetTransitionIndex(91.5m);

        var s2 = new PlayerSeasonStat(p2.Id, 2027, 25, 28.4m, 15.1m, 4.9m, 2.8m, 1.0m, 0.2m,
            0.45m, 0.36m, 0.85m, 2.3m, league: "EuroLeague", teamName: "Fenerbahçe Beko");
        s2.SetTransitionIndex(87.2m);

        var injury = new PlayerInjury(p2.Id, 2027, "Hamstring", "Questionable", DateTime.UtcNow, 1, "Rotowire EuroLeague", "Game-time decision");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartScoutDbContext>();
            db.Players.AddRange(p1, p2);
            db.SeasonStats.AddRange(s1, s2);
            db.PlayerInjuries.Add(injury);
            await db.SaveChangesAsync();
        }

        var resp = await _client.GetAsync("/api/players/teams/roster?league=EuroLeague&team=Fenerbah%C3%A7e%20Beko&season=2027");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var roster = await resp.Content.ReadFromJsonAsync<TeamRosterResponseDto>();
        Assert.NotNull(roster);
        Assert.Equal(2027, roster.Season);
        Assert.Equal(2, roster.PlayerCount);
        Assert.Equal(1, roster.ActiveInjuriesCount);
        Assert.Equal("Nigel Hayes-Davis", roster.Roster[0].FullName);
        Assert.Equal("Questionable", roster.Roster[1].ActiveInjuryStatus);
    }

    [Fact]
    public async Task ApiKeyProtectedEndpoints_ShouldRejectMissingQueryAndInvalidKey_AndAcceptValidHeader()
    {
        var missingKey = await _client.PostAsync("/api/players/enrich-bios?limit=0", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, missingKey.StatusCode);

        var queryParamKey = await _client.PostAsync(
            $"/api/players/enrich-bios?limit=0&apiKey={SmartScoutWebApplicationFactory.TestApiKey}",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, queryParamKey.StatusCode);

        using var wrongHeaderReq = new HttpRequestMessage(HttpMethod.Post, "/api/players/enrich-bios?limit=0");
        wrongHeaderReq.Headers.Add(ApiKeyAuthAttribute.ApiKeyHeaderName, "wrong-key");
        var wrongKeyResp = await _client.SendAsync(wrongHeaderReq);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyResp.StatusCode);

        // Valid header passes ApiKeyAuthAttribute and reaches FluentValidation (limit=0 -> 400 BadRequest, no external HTTP calls)
        using var validHeaderReq = new HttpRequestMessage(HttpMethod.Post, "/api/players/enrich-bios?limit=0");
        validHeaderReq.Headers.Add(ApiKeyAuthAttribute.ApiKeyHeaderName, SmartScoutWebApplicationFactory.TestApiKey);
        var validKeyResp = await _client.SendAsync(validHeaderReq);
        Assert.Equal(HttpStatusCode.BadRequest, validKeyResp.StatusCode);
    }

    [Fact]
    public async Task SeedDemoWatchlist_WithCustomBoard_SeedsBoardEntries_Returns404WhenMissing_AndPreservesPrimaryEti()
    {
        var p1 = new Player(4001, "Maxime", "Raynaud", "C", "7-1", 240, "SAC", "NBA", "FRA",
            new DateTime(2003, 4, 7, 0, 0, 0, DateTimeKind.Utc), 216, 109);
        var p2 = new Player(4002, "Guard", "Euro", "PG", "6-3", 190, "Fenerbahçe", "EuroLeague", "TUR",
            new DateTime(2002, 5, 12, 0, 0, 0, DateTimeKind.Utc), 191, 86);

        var cameo = new PlayerSeasonStat(p1.Id, 2026, 1, 37.0m, 14.0m, 2.0m, 8.0m, 1.0m, 1.0m,
            0.53m, 0.50m, 0.80m, 2.0m, league: "GLeague", teamName: "STO", fgm: 8.0m, fga: 15.0m, fg3m: 1.0m, fg3a: 2.0m);
        var nbaMain = new PlayerSeasonStat(p1.Id, 2026, 74, 26.5m, 11.2m, 1.8m, 6.8m, 0.6m, 0.7m,
            0.52m, 0.34m, 0.78m, 1.1m, league: "NBA", teamName: "SAC", fgm: 4.7m, fga: 9.1m, fg3m: 0.34m, fg3a: 1.0m);
        var guardStat = new PlayerSeasonStat(p2.Id, 2027, 22, 24.0m, 12.5m, 5.1m, 2.8m, 1.4m, 0.1m,
            0.48m, 0.40m, 0.86m, 1.5m, league: "EuroLeague", teamName: "FEN", fgm: 4.3m, fga: 9.0m, fg3m: 1.8m, fg3a: 4.5m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartScoutDbContext>();
            db.Players.AddRange(p1, p2);
            db.SeasonStats.AddRange(cameo, nbaMain, guardStat);
            await db.SaveChangesAsync();
        }

        // Nonexistent boardId -> 404
        var missingResp = await _client.PostAsync($"/api/players/watchlist/seed-demo?boardId={Guid.NewGuid()}", content: null);
        Assert.Equal(HttpStatusCode.NotFound, missingResp.StatusCode);

        // Create custom board
        var createResp = await _client.PostAsJsonAsync("/api/boards", new { name = "Demo Seed Board", ownerName = "Scout" });
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        var board = await createResp.Content.ReadFromJsonAsync<ScoutBoardSummaryDto>();
        Assert.NotNull(board);

        // Seed via query string
        var seedResp = await _client.PostAsync($"/api/players/watchlist/seed-demo?boardId={board.Id}", content: null);
        Assert.Equal(HttpStatusCode.OK, seedResp.StatusCode);
        var seedData = await seedResp.Content.ReadFromJsonAsync<SeedDemoWatchlistResultDto>();
        Assert.NotNull(seedData);
        Assert.Equal(2, seedData.AddedCount);
        Assert.Equal(2, seedData.TotalWatchlisted);

        // Re-seed via JSON body -> idempotent (no duplicates added)
        var reseedResp = await _client.PostAsJsonAsync("/api/players/watchlist/seed-demo", new { boardId = board.Id });
        Assert.Equal(HttpStatusCode.OK, reseedResp.StatusCode);
        var reseedData = await reseedResp.Content.ReadFromJsonAsync<SeedDemoWatchlistResultDto>();
        Assert.NotNull(reseedData);
        Assert.Equal(0, reseedData.AddedCount);
        Assert.Equal(2, reseedData.TotalWatchlisted);

        // Verify board players preserve primary 74-GP ETI and ThreePointRate
        var boardPlayers = await _client.GetFromJsonAsync<List<BoardPlayerDto>>($"/api/boards/{board.Id}/players");
        Assert.NotNull(boardPlayers);
        Assert.Equal(2, boardPlayers.Count);
        var raynaud = boardPlayers.Find(x => x.FullName == "Maxime Raynaud");
        Assert.NotNull(raynaud?.LatestStat);
        Assert.Equal(98.0m, raynaud!.LatestStat!.EuroLeagueTransitionIndex);
        Assert.Equal(0.110m, raynaud.LatestStat.ThreePointRate);

        // Default watchlist remains empty
        var defaultWatchlist = await _client.GetFromJsonAsync<List<WatchlistPlayerDto>>("/api/players/watchlist");
        Assert.NotNull(defaultWatchlist);
        Assert.Empty(defaultWatchlist);
    }
}
