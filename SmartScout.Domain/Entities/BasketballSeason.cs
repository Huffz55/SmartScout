using System;
using System.Collections.Generic;
using System.Globalization;

namespace SmartScout.Domain.Entities;

public class BasketballSeason
{
    public Guid Id { get; private set; }
    public string Label { get; private set; } = string.Empty; // e.g., "2023-24"
    public int StartYear { get; private set; }
    public int EndYear { get; private set; }
    public bool IsCurrent { get; private set; }

    private BasketballSeason() { }

    public BasketballSeason(int startYear, int endYear, bool isCurrent = false)
    {
        Id = Guid.NewGuid();
        StartYear = startYear;
        EndYear = endYear;
        Label = $"{startYear}-{endYear.ToString().Substring(2)}";
        IsCurrent = isCurrent;
    }

    public void SetCurrent(bool isCurrent)
    {
        IsCurrent = isCurrent;
    }

    /// <summary>
    /// Parses a season string like "2024-25", "2024-2025", "2024/25", or "2025" into its canonical EndYear (2025).
    /// </summary>
    public static int? ParseSeasonEndYear(string? seasonInput)
    {
        if (string.IsNullOrWhiteSpace(seasonInput))
            return null;

        string cleaned = seasonInput.Trim();
        var parts = cleaned.Split(new[] { '-', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int singleYear))
        {
            return (singleYear >= 1950 && singleYear <= 2100) ? singleYear : null;
        }

        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int startYear))
        {
            if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int secondPart))
            {
                if (secondPart < 100)
                {
                    int century = (startYear / 100) * 100;
                    int endYear = century + secondPart;
                    if (endYear < startYear) endYear += 100;
                    return endYear;
                }
                return secondPart;
            }
            return startYear + 1;
        }

        return null;
    }

    public static IReadOnlyList<BasketballSeason> GetDefaultSeedSeasons()
    {
        var list = new List<BasketballSeason>();
        for (int endYear = 2019; endYear <= 2027; endYear++)
        {
            list.Add(new BasketballSeason(endYear - 1, endYear, isCurrent: endYear == 2026 || endYear == 2027));
        }
        return list;
    }
}
