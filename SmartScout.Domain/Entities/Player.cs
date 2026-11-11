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
    public int? StandingReachCm { get; private set; }
    public int? DraftYear { get; private set; }
    public int? DraftPick { get; private set; }
    public string? TeamName { get; private set; }
    public string League { get; private set; } = string.Empty;
    public string? Nationality { get; private set; }
    public DateTime? BirthDate { get; private set; }
    public int? ContractExpirySeason { get; private set; }
    public string? EstimatedMarketValueTier { get; private set; }
    public string? AgencyName { get; private set; }
    public string DominantHand { get; private set; } = "Right";
    public string? ScoutNotes { get; private set; }
    public bool IsWatchlisted { get; private set; }

    /// <summary>
    /// Dynamically computed age based on BirthDate and the current UTC date.
    /// Never stored statically in DB; always derived on-the-fly.
    /// </summary>
    public int? CurrentAge
    {
        get
        {
            if (!BirthDate.HasValue) return null;
            var today = DateTime.UtcNow.Date;
            var dob = BirthDate.Value.Date;
            int age = today.Year - dob.Year;
            if (dob > today.AddYears(-age)) age--;
            return (age >= 14 && age <= 55) ? age : null;
        }
    }

    /// <summary>
    /// Computes age relative to a specific reference date (e.g. season end).
    /// </summary>
    public int? CalculateAgeAt(DateTime referenceDate)
    {
        if (!BirthDate.HasValue) return null;
        var refDate = referenceDate.Date;
        var dob = BirthDate.Value.Date;
        int age = refDate.Year - dob.Year;
        if (dob > refDate.AddYears(-age)) age--;
        return (age >= 14 && age <= 55) ? age : null;
    }

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

    public void SetScoutNotes(string? notes)
    {
        ScoutNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void ToggleWatchlist(bool? state = null)
    {
        IsWatchlisted = state ?? !IsWatchlisted;
    }

    public void UpdateScoutingIntel(
        int? standingReachCm = null,
        int? contractExpirySeason = null,
        string? estimatedMarketValueTier = null,
        string? agencyName = null,
        string? dominantHand = null)
    {
        if (standingReachCm.HasValue && standingReachCm.Value > 170 && standingReachCm.Value < 320)
            StandingReachCm = standingReachCm.Value;
        if (contractExpirySeason.HasValue)
            ContractExpirySeason = contractExpirySeason.Value;
        if (!string.IsNullOrWhiteSpace(estimatedMarketValueTier))
            EstimatedMarketValueTier = estimatedMarketValueTier.Trim();
        if (!string.IsNullOrWhiteSpace(agencyName))
            AgencyName = agencyName.Trim();
        if (!string.IsNullOrWhiteSpace(dominantHand))
            DominantHand = dominantHand.Trim();
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
            int cmVal = (int)Math.Round((feet * 12 + inches) * 2.54);
            if (cmVal >= 160 && cmVal <= 240) return cmVal;
            return null;
        }
        if (int.TryParse(height.Replace("cm", "", StringComparison.OrdinalIgnoreCase).Trim(), out int cm) && cm >= 160 && cm <= 240)
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
        "Adem Bona", "Metecan Birsen", "Mert Emre Eksioglu", "Can Mutaf",
        "Scottie Wilbekin", "Shane Larkin", "Muhsin Yasar", "Egehan Arna", "Yigitcan Saybir"
    };

    private static readonly HashSet<string> KnownTurkishFirstNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ahmet", "Mehmet", "Ali", "Mustafa", "Emre", "Can", "Kerem", "Berk", "Ege", "Egehan",
        "Arda", "Batuhan", "Yigit", "Yiğit", "Yagiz", "Yağız", "Furkan", "Sertac", "Sertaç",
        "Melih", "Onuralp", "Sehmus", "Şehmuz", "Ercan", "Bugrahan", "Buğrahan", "Dogus", "Doğuş",
        "Erkan", "Kenan", "Sadik", "Sadık", "Tarik", "Tarık", "Samet", "Omer", "Ömer", "Alperen",
        "Adem", "Metecan", "Mert", "Muhsin", "Tolga", "Berke", "Okben", "Burak", "Eren", "Hakan",
        "Gokhan", "Gökhan", "Serhat", "Oguz", "Oğuz", "Sinan", "Baris", "Barış", "Cem", "Doruk",
        "Kaan", "Emir", "Berkcan", "Berkan", "Enes", "Semih", "Ibrahim", "İbrahim", "Dogukan", "Doğukan",
        "Ata", "Bilal", "Tuncay", "Abdullah", "Mete", "Faruk", "Berkay", "Cagdas", "Çağdaş", "Deniz",
        "Ilkan", "İlkan", "Ilker", "İlker", "Ismail", "İsmail", "Kutay", "Mahir", "Murat", "Ogulcan",
        "Oğulcan", "Onur", "Ozgur", "Özgür", "Ridvan", "Rıdvan", "Sabri", "Sarp", "Sefa", "Serkan",
        "Taner", "Tugberk", "Tuğberk", "Ufuk", "Ugur", "Uğur", "Umut", "Volkan", "Yalcin", "Yalçın",
        "Yasin", "Zafer", "Ziya", "Alp", "Anil", "Anıl", "Arif", "Ateberk", "Batu", "Bekir",
        "Bertan", "Bilgehan", "Bora", "Bugra", "Buğra", "Cengiz", "Cengizhan", "Emin", "Engin",
        "Eray", "Erbey", "Erkin", "Eyup", "Eyüp", "Fatih", "Firat", "Fırat", "Goksenin", "Göksenin",
        "Goktug", "Göktuğ", "Halil", "Hamdi", "Hasan", "Kivanc", "Kıvanç", "Mertcan", "Omercan",
        "Ömercan", "Orhan", "Polat", "Recai", "Tevfik", "Tibet", "Toprak", "Tuna", "Yunus"
    };

    private static bool IsTurkishClub(string? teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName)) return false;
        string normalized = NormalizeName(teamName, "");
        return normalized.Contains("besiktas") ||
               normalized.Contains("fenerbahce") ||
               normalized.Contains("efes") ||
               normalized.Contains("bahcesehir") ||
               normalized.Contains("telekom") ||
               normalized.Contains("karsiyaka") ||
               normalized.Contains("bursa") ||
               normalized.Contains("tofas") ||
               normalized.Contains("darussafaka") ||
               normalized.Contains("galatasaray") ||
               normalized.Contains("manisa") ||
               normalized.Contains("petkim") ||
               normalized.Contains("buyukcekmece") ||
               normalized.Contains("merkezefendi") ||
               normalized.Contains("yalova") ||
               normalized.Contains("samsun") ||
               normalized.Contains("trabzon") ||
               normalized.Contains("mersin") ||
               normalized.Contains("konya") ||
               normalized.Contains("bodrum") ||
               normalized.Contains("ormanspor") ||
               normalized.Contains("afyon") ||
               normalized.Contains("fethiye") ||
               normalized.Contains("ted ankara") ||
               normalized.Contains("gaziantep");
    }

    public static string? DetectNationality(string firstName, string lastName, string? teamName = null)
    {
        string fullName = $"{firstName} {lastName}".Trim();
        if (KnownTurkishNames.Contains(fullName))
        {
            return "TUR";
        }

        if (IsTurkishClub(teamName))
        {
            string normFirst = NormalizeName(firstName?.Trim() ?? string.Empty, "");
            if (KnownTurkishFirstNames.Contains(firstName?.Trim() ?? string.Empty) ||
                KnownTurkishFirstNames.Contains(normFirst))
            {
                return "TUR";
            }

            string normLast = NormalizeName(lastName?.Trim() ?? string.Empty, "");
            if (normLast.EndsWith("oglu") ||
                normLast.EndsWith("kaya") ||
                normLast.EndsWith("demir") ||
                normLast.EndsWith("yilmaz") ||
                normLast.EndsWith("senel") ||
                normLast.EndsWith("aksu") ||
                normLast.EndsWith("ozbek") ||
                normLast.EndsWith("turk"))
            {
                return "TUR";
            }
        }

        return null;
    }
}
