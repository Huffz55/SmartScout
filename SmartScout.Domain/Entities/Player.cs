using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SmartScout.Domain.Entities;

public class Player
{
    public Guid Id { get; private set; }
    public int ExternalApiId { get; private set; } // Primary external identifier
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty; // Accent-insensitive indexed name for cross-source matching
    public string? Position { get; private set; }
    public string? PrimaryPosition { get; private set; }
    public string? SecondaryPosition { get; private set; }
    public string? Height { get; private set; }
    public int? HeightCm { get; private set; }
    public int? WeightPounds { get; private set; }
    public int? WeightKg { get; private set; }
    public int? WingspanCm { get; private set; }
    public int? DraftYear { get; private set; }
    public int? DraftPick { get; private set; }
    public string? TeamName { get; private set; }
    public string League { get; private set; } = string.Empty;
    public string? Nationality { get; private set; }
    public DateTime? BirthDate { get; private set; }
    public string? ExternalIdsJson { get; private set; } // JSONB mapping across providers (Balldontlie, BRef, EuroLeague, RealGM)

    private readonly List<PlayerSeasonStat> _seasonStats = new();
    public IReadOnlyCollection<PlayerSeasonStat> SeasonStats => _seasonStats.AsReadOnly();

    // EF Core constructor
    private Player() { }

    public Player(
        int externalApiId, 
        string firstName, 
        string lastName, 
        string? position, 
        string? height, 
        int? weightPounds, 
        string? teamName, 
        string league, 
        string? nationality = null, 
        DateTime? birthDate = null,
        int? heightCm = null,
        int? weightKg = null,
        int? wingspanCm = null,
        int? draftYear = null,
        int? draftPick = null,
        string? externalIdsJson = null)
    {
        Id = Guid.NewGuid();
        ExternalApiId = externalApiId;
        FirstName = firstName?.Trim() ?? string.Empty;
        LastName = lastName?.Trim() ?? string.Empty;
        FullName = $"{FirstName} {LastName}".Trim();
        NormalizedName = NormalizeName(FirstName, LastName);
        Position = position;
        (PrimaryPosition, SecondaryPosition) = ParsePositions(position);
        Height = height;
        HeightCm = heightCm ?? ConvertHeightToCm(height);
        WeightPounds = weightPounds;
        WeightKg = weightKg ?? ConvertPoundsToKg(weightPounds);
        WingspanCm = wingspanCm;
        DraftYear = draftYear;
        DraftPick = draftPick;
        TeamName = teamName;
        League = league;
        Nationality = nationality;
        BirthDate = birthDate.HasValue 
            ? (birthDate.Value.Kind == DateTimeKind.Unspecified 
                ? DateTime.SpecifyKind(birthDate.Value, DateTimeKind.Utc) 
                : birthDate.Value.ToUniversalTime()) 
            : null;
        ExternalIdsJson = externalIdsJson;
    }

    public void UpdateDetails(
        string firstName, 
        string lastName, 
        string? position, 
        string? height, 
        int? weightPounds, 
        string? teamName, 
        string league, 
        string? nationality = null, 
        DateTime? birthDate = null,
        int? heightCm = null,
        int? weightKg = null,
        int? wingspanCm = null,
        int? draftYear = null,
        int? draftPick = null,
        string? externalIdsJson = null)
    {
        FirstName = firstName?.Trim() ?? FirstName;
        LastName = lastName?.Trim() ?? LastName;
        FullName = $"{FirstName} {LastName}".Trim();
        NormalizedName = NormalizeName(FirstName, LastName);
        Position = position ?? Position;
        var (prim, sec) = ParsePositions(Position);
        PrimaryPosition = prim ?? PrimaryPosition;
        SecondaryPosition = sec ?? SecondaryPosition;
        Height = height ?? Height;
        HeightCm = heightCm ?? ConvertHeightToCm(Height) ?? HeightCm;
        WeightPounds = weightPounds ?? WeightPounds;
        WeightKg = weightKg ?? ConvertPoundsToKg(WeightPounds) ?? WeightKg;
        WingspanCm = wingspanCm ?? WingspanCm;
        DraftYear = draftYear ?? DraftYear;
        DraftPick = draftPick ?? DraftPick;
        TeamName = teamName ?? TeamName;
        League = !string.IsNullOrWhiteSpace(league) ? league : League;
        Nationality = nationality ?? Nationality;
        BirthDate = birthDate.HasValue 
            ? (birthDate.Value.Kind == DateTimeKind.Unspecified 
                ? DateTime.SpecifyKind(birthDate.Value, DateTimeKind.Utc) 
                : birthDate.Value.ToUniversalTime()) 
            : BirthDate;
        ExternalIdsJson = externalIdsJson ?? ExternalIdsJson;
    }

    public void AddSeasonStat(PlayerSeasonStat stat)
    {
        _seasonStats.Add(stat);
    }

    public static string NormalizeName(string? firstName, string? lastName)
    {
        string raw = $"{firstName} {lastName}".Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // Replace Turkish 'İ'/'ı' and European special characters before FormD decomposition
        raw = raw.Replace('İ', 'i').Replace('ı', 'i').Replace('ß', 's').Replace('đ', 'd').Replace('ł', 'l');

        string normalized = raw.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private static (string? Primary, string? Secondary) ParsePositions(string? position)
    {
        if (string.IsNullOrWhiteSpace(position) || position.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var parts = position.Split(new[] { '-', '/', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1) return (parts[0], null);
        if (parts.Length >= 2) return (parts[0], parts[1]);
        return (null, null);
    }

    private static int? ConvertHeightToCm(string? height)
    {
        if (string.IsNullOrWhiteSpace(height)) return null;
        var parts = height.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out int feet) && int.TryParse(parts[1], out int inches))
        {
            return (int)Math.Round((feet * 12 + inches) * 2.54);
        }
        if (int.TryParse(height.Replace("cm", "", StringComparison.OrdinalIgnoreCase).Trim(), out int cm) && cm > 140 && cm < 250)
        {
            return cm;
        }
        return null;
    }

    private static int? ConvertPoundsToKg(int? weightPounds)
    {
        if (!weightPounds.HasValue || weightPounds.Value <= 0) return null;
        return (int)Math.Round(weightPounds.Value * 0.45359237);
    }

    private static readonly HashSet<string> KnownTurkishNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Berk Ugurlu", "Yigit Arslan", "Kerem Konan", "Ata Ozbek", "Yagiz Aksu",
        "Cedi Osman", "Furkan Korkmaz", "Sertac Sanli", "Melih Mahmutoglu",
        "Onuralp Bitim", "Sehmus Hazer", "Ercan Osmani", "Bugrahan Tuncer",
        "Dogus Ozdemiroglu", "Erkan Yilmaz", "Kenan Sipahi", "Sadik Emir Kabaca",
        "Tarik Biberovic", "Samet Yigitoglu", "Omer Faruk Yurtseven", "Alperen Sengun",
        "Adem Bona", "Metecan Birsen", "Mert Emre Eksioglu", "Can Mutaf"
    };

    public static string? DetectNationality(string firstName, string lastName, string? teamName = null)
    {
        string fullName = $"{firstName} {lastName}".Trim();
        if (KnownTurkishNames.Contains(fullName))
        {
            return "TUR";
        }

        if (!string.IsNullOrEmpty(teamName) && 
            (teamName.Contains("BESIKTAS", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("FENERBAHCE", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("EFES", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("BAHCESEHIR", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("TELEKOM", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("KARS", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("BURSA", StringComparison.OrdinalIgnoreCase) ||
             teamName.Contains("TOFAS", StringComparison.OrdinalIgnoreCase)))
        {
            if (lastName.EndsWith("oglu", StringComparison.OrdinalIgnoreCase) ||
                lastName.EndsWith("oğlu", StringComparison.OrdinalIgnoreCase) ||
                lastName.EndsWith("li", StringComparison.OrdinalIgnoreCase) ||
                lastName.EndsWith("ci", StringComparison.OrdinalIgnoreCase) ||
                lastName.EndsWith("cu", StringComparison.OrdinalIgnoreCase))
            {
                return "TUR";
            }
        }

        return null;
    }
}
