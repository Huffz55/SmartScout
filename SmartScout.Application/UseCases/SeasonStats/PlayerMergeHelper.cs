using System;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.UseCases.SeasonStats;

/// <summary>
/// Non-destructive merge policies for preserving enriched player metadata when ingesting table stats.
/// </summary>
public static class PlayerMergeHelper
{
    /// <summary>
    /// Resolves player position without downgrading specific positions (e.g., PG/SG, PF) to coarse labels (G, F).
    /// </summary>
    public static string? ResolvePosition(string? existingPosition, string? incomingPosition)
    {
        if (string.IsNullOrWhiteSpace(existingPosition))
            return string.IsNullOrWhiteSpace(incomingPosition) ? null : incomingPosition.Trim();

        if (string.IsNullOrWhiteSpace(incomingPosition))
            return existingPosition.Trim();

        string existing = existingPosition.Trim();
        string incoming = incomingPosition.Trim();

        return GetPositionSpecificity(incoming) > GetPositionSpecificity(existing)
            ? incoming
            : existing;
    }

    /// <summary>
    /// Resolves player nationality without overwriting a known nationality with placeholder values such as INTL.
    /// </summary>
    public static string ResolveNationality(
        string? existingNationality,
        string? incomingNationality,
        string? firstName,
        string? lastName,
        string? teamName)
    {
        if (IsRealNationality(existingNationality))
            return existingNationality!.Trim();

        if (IsRealNationality(incomingNationality))
            return incomingNationality!.Trim();

        string? detected = Player.DetectNationality(firstName ?? string.Empty, lastName ?? string.Empty, teamName ?? string.Empty);
        if (IsRealNationality(detected))
            return detected!.Trim();

        return !string.IsNullOrWhiteSpace(existingNationality)
            ? existingNationality.Trim()
            : (!string.IsNullOrWhiteSpace(incomingNationality) ? incomingNationality.Trim() : "INTL");
    }

    /// <summary>
    /// Preserves an existing birth date before falling back to an incoming or age-estimated birth date.
    /// </summary>
    public static DateTime? ResolveBirthDate(DateTime? existingBirthDate, DateTime? incomingBirthDate)
        => existingBirthDate ?? incomingBirthDate;

    private static bool IsRealNationality(string? nationality)
    {
        if (string.IsNullOrWhiteSpace(nationality))
            return false;

        string trimmed = nationality.Trim();
        return !trimmed.Equals("INTL", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Equals("UNK", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Equals("-", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPositionSpecificity(string position)
    {
        string normalized = position.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        string[] parts = normalized.Split(new[] { '/', '-', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool hasSpecificSlot = false;
        foreach (string part in parts)
        {
            if (part is "PG" or "SG" or "SF" or "PF")
            {
                hasSpecificSlot = true;
                break;
            }
        }

        if (parts.Length >= 2)
            return hasSpecificSlot ? 4 : 2;

        return normalized switch
        {
            "PG" or "SG" or "SF" or "PF" => 3,
            "C" => 2,
            _ => 1
        };
    }
}
