using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartScout.Application.DTOs;

public class SyncFilterContext
{
    public string? LeagueCode { get; init; }
    public int? Season { get; init; }
    public bool Force { get; init; }
    public IReadOnlySet<string> CompletedJobKeys { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static string BuildJobKey(string leagueCode, int season) =>
        $"{leagueCode.Trim()}|{season}";

    public bool IsAllLeagues =>
        string.IsNullOrWhiteSpace(LeagueCode) ||
        string.Equals(LeagueCode.Trim(), "all", StringComparison.OrdinalIgnoreCase);

    public HashSet<string> GetTargetLeagueCodes()
    {
        if (IsAllLeagues)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return LeagueCode!
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool HasExplicitLeague(string candidateLeagueCode)
    {
        if (string.IsNullOrWhiteSpace(LeagueCode))
            return false;

        var targets = GetTargetLeagueCodes();
        return targets.Contains(candidateLeagueCode.Trim());
    }

    public bool ShouldProcessLeague(string candidateLeagueCode)
    {
        if (IsAllLeagues)
            return true;

        var targetLeagues = GetTargetLeagueCodes();
        if (targetLeagues.Contains("all"))
            return true;

        return targetLeagues.Contains(candidateLeagueCode.Trim());
    }

    public bool ShouldProcessSeason(int candidateSeason)
    {
        if (!Season.HasValue)
            return true;

        return Season.Value == candidateSeason;
    }

    public bool IsAlreadyCompleted(string leagueCode, int season)
    {
        if (Force)
            return false;

        return CompletedJobKeys.Contains(BuildJobKey(leagueCode, season));
    }

    public bool ShouldProcess(string leagueCode, int season)
    {
        return ShouldProcessLeague(leagueCode)
            && ShouldProcessSeason(season)
            && !IsAlreadyCompleted(leagueCode, season);
    }
}
