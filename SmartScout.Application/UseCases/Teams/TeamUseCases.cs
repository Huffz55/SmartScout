using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Common.Queries;
using SmartScout.Application.Interfaces;
using SmartScout.Application.Services;

namespace SmartScout.Application.UseCases.Teams;

public sealed record TeamSummaryDto(
    string League,
    string TeamName,
    int Season,
    int PlayerCount,
    double AvgAge,
    double AvgEti,
    double AvgPoints,
    int ActiveInjuriesCount);

public sealed record TeamRosterPlayerDto(
    Guid PlayerId,
    string FullName,
    string? Position,
    string? Nationality,
    int? Age,
    int? HeightCm,
    string League,
    string TeamName,
    int Season,
    int GamesPlayed,
    decimal? Minutes,
    decimal? Points,
    decimal? Rebounds,
    decimal? Assists,
    decimal? PtsPer36,
    decimal? TrueShootingPct,
    decimal? Fg3Pct,
    decimal? EuroLeagueTransitionIndex,
    string? ActiveInjuryStatus,
    string? ActiveInjuryDetail);

public sealed record TeamRosterResponseDto(
    string TeamName,
    string? League,
    int Season,
    int PlayerCount,
    int ActiveInjuriesCount,
    List<TeamRosterPlayerDto> Roster);

public record GetTeamsSummaryQuery(
    string? League = null,
    int Season = 2027,
    string? Search = null,
    int Top = 60) : IRequest<List<TeamSummaryDto>>;

public class GetTeamsSummaryQueryValidator : AbstractValidator<GetTeamsSummaryQuery>
{
    public GetTeamsSummaryQueryValidator()
    {
        RuleFor(x => x.Season).InclusiveBetween(1946, 2100);
        RuleFor(x => x.Top).InclusiveBetween(1, 200);
    }
}

public class GetTeamsSummaryQueryHandler : IRequestHandler<GetTeamsSummaryQuery, List<TeamSummaryDto>>
{
    private readonly ISmartScoutDbContext _dbContext;
    private readonly IPlayerDirectorySearchFilter? _searchFilter;

    public GetTeamsSummaryQueryHandler(
        ISmartScoutDbContext dbContext,
        IPlayerDirectorySearchFilter? searchFilter = null)
    {
        _dbContext = dbContext;
        _searchFilter = searchFilter;
    }

    public async Task<List<TeamSummaryDto>> Handle(GetTeamsSummaryQuery request, CancellationToken cancellationToken)
    {
        var statsQuery = _dbContext.SeasonStats
            .AsNoTracking()
            .Where(s => s.Season == request.Season
                && !string.IsNullOrWhiteSpace(s.TeamName)
                && s.TeamName != "TOT"
                && s.TeamName != "2TM"
                && s.TeamName != "3TM");

        if (!string.IsNullOrWhiteSpace(request.League) && !string.Equals(request.League, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            string trimmedLeague = request.League.Trim();
            statsQuery = statsQuery.Where(s => s.League == trimmedLeague);
        }

        if (!string.IsNullOrWhiteSpace(request.Search) && _searchFilter != null)
        {
            statsQuery = _searchFilter.ApplyTeamSearchFilter(statsQuery, request.Search);
        }

        var rows = await statsQuery
            .Select(s => new
            {
                s.League,
                TeamName = s.TeamName!,
                s.PlayerId,
                BirthDate = s.Player != null ? s.Player.BirthDate : null,
                s.EuroLeagueTransitionIndex,
                s.Points
            })
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Search) && _searchFilter == null)
        {
            string term = request.Search.Trim();
            rows = rows
                .Where(r => r.TeamName.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || r.League.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var activeInjuredIds = await _dbContext.PlayerInjuries
            .AsNoTracking()
            .Where(InjuryPredicates.IsActive)
            .Select(i => i.PlayerId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var injuredSet = activeInjuredIds.ToHashSet();

        return rows
            .GroupBy(r => (r.League, r.TeamName))
            .Select(g =>
            {
                var ages = g
                    .Select(x => Domain.Entities.Player.CalculateAgeFromBirthDate(x.BirthDate))
                    .Where(a => a.HasValue)
                    .Select(a => (double)a!.Value)
                    .ToList();
                return new TeamSummaryDto(
                    g.Key.League,
                    g.Key.TeamName,
                    request.Season,
                    g.Select(x => x.PlayerId).Distinct().Count(),
                    ages.DefaultIfEmpty(0).Average(),
                    g.Where(x => x.EuroLeagueTransitionIndex.HasValue).Select(x => (double)x.EuroLeagueTransitionIndex!.Value).DefaultIfEmpty(0).Average(),
                    g.Where(x => x.Points.HasValue).Select(x => (double)x.Points!.Value).DefaultIfEmpty(0).Average(),
                    g.Select(x => x.PlayerId).Distinct().Count(id => injuredSet.Contains(id)));
            })
            .OrderByDescending(x => x.AvgEti)
            .ThenByDescending(x => x.PlayerCount)
            .Take(Math.Clamp(request.Top, 1, 200))
            .ToList();
    }
}

public record GetTeamRosterQuery(
    string? League,
    string Team,
    int? Season = null) : IRequest<TeamRosterResponseDto>;

public class GetTeamRosterQueryValidator : AbstractValidator<GetTeamRosterQuery>
{
    public GetTeamRosterQueryValidator()
    {
        RuleFor(x => x.Team)
            .NotEmpty().WithMessage("Team parameter is required.");
    }
}

public class GetTeamRosterQueryHandler : IRequestHandler<GetTeamRosterQuery, TeamRosterResponseDto>
{
    private readonly ISmartScoutDbContext _dbContext;

    public GetTeamRosterQueryHandler(ISmartScoutDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TeamRosterResponseDto> Handle(GetTeamRosterQuery request, CancellationToken cancellationToken)
    {
        var lowerVariants = SleeperScoutingService
            .GetTeamNameVariants(request.Team)
            .Select(v => v.ToLowerInvariant())
            .ToList();

        int? resolvedMaxSeason = await _dbContext.SeasonStats
            .AsNoTracking()
            .Where(s => s.TeamName != null
                && lowerVariants.Contains(s.TeamName.ToLower())
                && (string.IsNullOrWhiteSpace(request.League) || s.League == request.League))
            .MaxAsync(s => (int?)s.Season, cancellationToken);

        int targetSeason = request.Season ?? resolvedMaxSeason ?? 2027;

        async Task<List<TeamRosterRow>> QuerySeasonRosterAsync(int querySeason)
        {
            var q = _dbContext.SeasonStats
                .AsNoTracking()
                .Where(s => s.Season == querySeason
                    && s.TeamName != null
                    && lowerVariants.Contains(s.TeamName.ToLower()));

            if (!string.IsNullOrWhiteSpace(request.League))
            {
                q = q.Where(s => s.League == request.League);
            }

            return await q
                .OrderByDescending(s => s.EuroLeagueTransitionIndex ?? 0m)
                .ThenByDescending(s => s.Points ?? 0m)
                .Select(s => new TeamRosterRow(
                    s.PlayerId,
                    s.Player != null ? s.Player.FullName : "Unknown",
                    s.Player != null ? (s.Player.PrimaryPosition ?? s.Player.Position) : null,
                    s.Player != null ? s.Player.Nationality : null,
                    s.Player != null ? s.Player.BirthDate : null,
                    s.Player != null ? s.Player.HeightCm : null,
                    s.League,
                    s.TeamName!,
                    s.Season,
                    s.GamesPlayed,
                    s.Minutes,
                    s.Points,
                    s.Rebounds,
                    s.Assists,
                    s.PtsPer36,
                    s.TrueShootingPct,
                    s.Fg3Pct,
                    s.EuroLeagueTransitionIndex))
                .ToListAsync(cancellationToken);
        }

        var rawRoster = await QuerySeasonRosterAsync(targetSeason);
        if (rawRoster.Count == 0 && request.Season.HasValue && resolvedMaxSeason.HasValue && resolvedMaxSeason.Value != targetSeason)
        {
            targetSeason = resolvedMaxSeason.Value;
            rawRoster = await QuerySeasonRosterAsync(targetSeason);
        }

        var roster = rawRoster
            .GroupBy(r => r.PlayerId)
            .Select(g => g.First())
            .ToList();

        var rosterIds = roster.Select(r => r.PlayerId).Distinct().ToList();
        var injuries = await _dbContext.PlayerInjuries
            .AsNoTracking()
            .Where(InjuryPredicates.IsActive)
            .Where(i => rosterIds.Contains(i.PlayerId))
            .OrderByDescending(i => i.ReportedAt)
            .ToListAsync(cancellationToken);

        var injByPlayer = injuries.GroupBy(i => i.PlayerId).ToDictionary(g => g.Key, g => g.First());

        var enrichedRoster = roster.Select(r =>
        {
            injByPlayer.TryGetValue(r.PlayerId, out var inj);
            return new TeamRosterPlayerDto(
                r.PlayerId,
                r.FullName,
                r.Position,
                r.Nationality,
                Domain.Entities.Player.CalculateAgeFromBirthDate(r.BirthDate),
                r.HeightCm,
                r.League,
                r.TeamName,
                r.Season,
                r.GamesPlayed,
                r.Minutes,
                r.Points,
                r.Rebounds,
                r.Assists,
                r.PtsPer36,
                r.TrueShootingPct,
                r.Fg3Pct,
                r.EuroLeagueTransitionIndex,
                inj?.Status,
                inj?.InjuryType);
        }).ToList();

        return new TeamRosterResponseDto(
            request.Team,
            request.League,
            targetSeason,
            enrichedRoster.Count,
            enrichedRoster.Count(r => r.ActiveInjuryStatus != null),
            enrichedRoster);
    }

    private sealed record TeamRosterRow(
        Guid PlayerId,
        string FullName,
        string? Position,
        string? Nationality,
        DateTime? BirthDate,
        int? HeightCm,
        string League,
        string TeamName,
        int Season,
        int GamesPlayed,
        decimal? Minutes,
        decimal? Points,
        decimal? Rebounds,
        decimal? Assists,
        decimal? PtsPer36,
        decimal? TrueShootingPct,
        decimal? Fg3Pct,
        decimal? EuroLeagueTransitionIndex);
}
