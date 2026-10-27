using System.Collections.Generic;
using SmartScout.Application.DTOs;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Interfaces;

public interface ISleeperScoutingService
{
    List<SleeperPlayerDto> DiscoverSleepers(
        IEnumerable<PlayerSeasonStat> allSeasonStats,
        string? categoryFilter = null,
        string? leagueFilter = null,
        int? seasonFilter = null,
        int top = 25);
}
