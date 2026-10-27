using System;
using System.Globalization;
using System.Text;

namespace SmartScout.Infrastructure.Services.EuroLeague;

public static class EuroLeagueParserHelper
{
    private static readonly HashSet<string> KnownTurkishNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Berk Ugurlu", "Yigit Arslan", "Kerem Konan", "Ata Ozbek", "Yagiz Aksu",
        "Cedi Osman", "Furkan Korkmaz", "Sertac Sanli", "Melih Mahmutoglu",
        "Onuralp Bitim", "Sehmus Hazer", "Ercan Osmani", "Bugrahan Tuncer",
        "Dogus Ozdemiroglu", "Erkan Yilmaz", "Kenan Sipahi", "Sadik Emir Kabaca",
        "Tarik Biberovic", "Samet Yigitoglu", "Omer Faruk Yurtseven", "Alperen Sengun",
        "Adem Bona", "Metecan Birsen", "Mert Emre Eksioglu", "Can Mutaf"
    };

    public static (string FirstName, string LastName) ParsePlayerName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return ("Unknown", "Player");

        var parts = rawName.Split(',', 2, StringSplitOptions.TrimEntries);
        var textInfo = CultureInfo.InvariantCulture.TextInfo;

        if (parts.Length == 2)
        {
            string lastName = textInfo.ToTitleCase(parts[0].ToLowerInvariant());
            string firstName = textInfo.ToTitleCase(parts[1].ToLowerInvariant());
            return (firstName, lastName);
        }

        var spaceParts = rawName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (spaceParts.Length == 2)
        {
            return (textInfo.ToTitleCase(spaceParts[0].ToLowerInvariant()), textInfo.ToTitleCase(spaceParts[1].ToLowerInvariant()));
        }

        return (textInfo.ToTitleCase(rawName.ToLowerInvariant()), string.Empty);
    }

    public static string? DetectNationality(string firstName, string lastName, string? teamName)
    {
        return SmartScout.Domain.Entities.Player.DetectNationality(firstName, lastName, teamName);
    }

    private static string? OldDetectNationality(string firstName, string lastName, string? teamName)
    {
        string fullName = $"{firstName} {lastName}".Trim();
        if (KnownTurkishNames.Contains(fullName))
        {
            return "TUR";
        }

        // If playing for Turkish team and has Turkish naming hints
        if (!string.IsNullOrEmpty(teamName) && 
            (teamName.Contains("BESIKTAS", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("FENERBAHCE", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("EFES", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("BAHCESEHIR", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("TELEKOM", StringComparison.OrdinalIgnoreCase)))
        {
            if (lastName.EndsWith("oglu", StringComparison.OrdinalIgnoreCase) ||
                lastName.EndsWith("oglu", StringComparison.OrdinalIgnoreCase) ||
                lastName.EndsWith("li", StringComparison.OrdinalIgnoreCase) ||
                lastName.EndsWith("ci", StringComparison.OrdinalIgnoreCase))
            {
                return "TUR";
            }
        }

        return null;
    }

    public static decimal ParseMinutes(string? minutesStr)
    {
        if (string.IsNullOrWhiteSpace(minutesStr) || 
            minutesStr.Equals("DNP", StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        var parts = minutesStr.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && 
            decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal mins) &&
            decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal secs))
        {
            return Math.Round(mins + (secs / 60m), 1);
        }

        if (decimal.TryParse(minutesStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal directMins))
        {
            return directMins;
        }

        return 0m;
    }

    public static int GenerateDeterministicId(string uniqueKey)
    {
        // 32-bit FNV-1a deterministic negative ID hashing
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(uniqueKey))
        {
            hash = (hash ^ b) * 16777619;
        }
        int intHash = unchecked((int)hash);
        int negativeId = -Math.Abs(intHash == int.MinValue ? int.MaxValue : intHash);
        return negativeId == 0 ? -999999 : negativeId;
    }
}
