using System;

namespace SmartScout.Domain.Entities;

/// <summary>
/// Tracks verified medical and injury records per player and season.
/// </summary>
public class PlayerInjury
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public int Season { get; private set; }
    public string InjuryType { get; private set; } = string.Empty;
    public int GamesMissed { get; private set; }
    public string Status { get; private set; } = "Out";
    public DateTime ReportedAt { get; private set; }
    public string? Source { get; private set; }
    public string? Notes { get; private set; }

    public Player? Player { get; private set; }

    public bool IsActive =>
        !string.Equals(Status, "Recovered", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "Healthy", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "Cleared", StringComparison.OrdinalIgnoreCase);

    private PlayerInjury() { }

    public PlayerInjury(
        Guid playerId,
        int season,
        string injuryType,
        string status,
        DateTime reportedAt,
        int gamesMissed = 0,
        string? source = null,
        string? notes = null)
    {
        Id = Guid.NewGuid();
        PlayerId = playerId;
        Season = season;
        InjuryType = NormalizeField(injuryType, 100, "Undisclosed");
        Status = NormalizeStatus(status);
        GamesMissed = Math.Max(0, gamesMissed);
        ReportedAt = EnsureUtc(reportedAt);
        Source = string.IsNullOrWhiteSpace(source) ? null : NormalizeField(source, 60, string.Empty);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : NormalizeField(notes, 350, string.Empty);
    }

    public void UpdateReport(
        string status,
        string injuryType,
        DateTime reportedAt,
        string? source = null,
        string? notes = null,
        int? gamesMissed = null)
    {
        if (!string.IsNullOrWhiteSpace(injuryType))
        {
            InjuryType = NormalizeField(injuryType, 100, InjuryType);
        }

        Status = NormalizeStatus(status);
        ReportedAt = EnsureUtc(reportedAt);

        if (gamesMissed.HasValue && gamesMissed.Value >= 0)
        {
            GamesMissed = gamesMissed.Value;
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            Source = NormalizeField(source, 60, string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = NormalizeField(notes, 350, string.Empty);
        }
    }

    public void MarkRecovered(DateTime recoveredAt, string? source = null)
    {
        Status = "Recovered";
        ReportedAt = EnsureUtc(recoveredAt);
        if (!string.IsNullOrWhiteSpace(source))
        {
            Source = NormalizeField(source, 60, string.Empty);
        }
    }

    public static string NormalizeStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Out";
        var s = rawStatus.Trim();
        var lower = s.ToLowerInvariant();
        if (lower.Contains("recovered") || lower.Contains("healthy") || lower.Contains("cleared")) return "Recovered";
        if (lower.Contains("out for season") || lower.Contains("ofs") || lower.Contains("indefinitely")) return "Out (Long-Term)";
        if (lower == "out" || lower.StartsWith("out ") || lower.Contains("ruled out")) return "Out";
        if (lower.Contains("doubtful")) return "Doubtful";
        if (lower.Contains("questionable")) return "Questionable";
        if (lower.Contains("game time") || lower.Contains("gtd")) return "Game Time Decision";
        if (lower.Contains("day-to-day") || lower.Contains("day to day") || lower.Contains("dtd")) return "Day-To-Day";
        if (lower.Contains("probable")) return "Probable";
        return NormalizeField(s, 50, "Out");
    }

    private static string NormalizeField(string? value, int maxLen, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen].TrimEnd();
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();
}
