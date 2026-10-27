using System;
using System.Collections.Generic;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.DTOs;

public class SeasonStatSyncBatchResult
{
    public IReadOnlyList<PlayerSeasonStat> Stats { get; init; } = Array.Empty<PlayerSeasonStat>();
    public IReadOnlyList<Guid> PhantomPlayerIdsToDelete { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Player> DiscoveredPlayers { get; init; } = Array.Empty<Player>();
    public string? LeagueCode { get; init; }
    public int? Season { get; init; }
    public string JobStatus { get; init; } = "Completed";
    public string? ErrorMessage { get; init; }

    public SeasonStatSyncBatchResult(
        IReadOnlyList<PlayerSeasonStat> stats, 
        IReadOnlyList<Guid> phantomPlayerIdsToDelete,
        string? leagueCode = null,
        int? season = null,
        IReadOnlyList<Player>? discoveredPlayers = null,
        string jobStatus = "Completed",
        string? errorMessage = null)
    {
        Stats = stats;
        PhantomPlayerIdsToDelete = phantomPlayerIdsToDelete;
        LeagueCode = leagueCode;
        Season = season;
        DiscoveredPlayers = discoveredPlayers ?? Array.Empty<Player>();
        JobStatus = jobStatus;
        ErrorMessage = errorMessage;
    }
}
