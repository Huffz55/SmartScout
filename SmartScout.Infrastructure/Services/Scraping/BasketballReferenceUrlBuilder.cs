using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SmartScout.Infrastructure.Services.Scraping;

public class BasketballReferenceUrlBuilder
{
    private const string BaseUrl = "https://www.basketball-reference.com/players";

    public (string slug, string url)? GeneratePlayerUrl(string firstName, string lastName, int suffixIndex)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return null;

        string cleanLastName = RemoveSuffixes(lastName);
        string cleanFirstName = RemoveSuffixes(firstName);

        cleanLastName = new string(RemoveDiacritics(cleanLastName).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        cleanFirstName = new string(RemoveDiacritics(cleanFirstName).Where(char.IsLetter).ToArray()).ToLowerInvariant();

        if (cleanLastName.Length == 0 || cleanFirstName.Length == 0)
            return null;

        string slugLastName = cleanLastName.Length > 5 ? cleanLastName.Substring(0, 5) : cleanLastName;
        string slugFirstName = cleanFirstName.Length > 2 ? cleanFirstName.Substring(0, 2) : cleanFirstName;
        string initial = slugLastName.Substring(0, 1);

        string suffix = suffixIndex.ToString("D2");
        string slug = $"{initial}/{slugLastName}{slugFirstName}{suffix}.html";
        string url = $"{BaseUrl}/{slug}";

        return (slug, url);
    }

    public bool IsPlayerMatch(string pagePlayerName, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(pagePlayerName)) return true; // Can't verify, don't reject blindly

        string cleanLastName = RemoveSuffixes(lastName);
        string cleanFirstName = RemoveSuffixes(firstName);

        cleanLastName = new string(RemoveDiacritics(cleanLastName).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        cleanFirstName = new string(RemoveDiacritics(cleanFirstName).Where(char.IsLetter).ToArray()).ToLowerInvariant();

        string normalizedPageName = new string(RemoveDiacritics(pagePlayerName).Where(char.IsLetter).ToArray()).ToLowerInvariant();

        string prefixFirst = cleanFirstName.Length >= 4 ? cleanFirstName.Substring(0, 4) : cleanFirstName;
        return normalizedPageName.Contains(cleanLastName) && 
               (normalizedPageName.Contains(cleanFirstName) || normalizedPageName.Contains(prefixFirst));
    }

    public string RemoveSuffixes(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        
        string[] suffixes = { " Jr.", " Sr.", " II", " III", " IV" };
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - suffix.Length);
            }
        }
        return name.Trim();
    }

    public string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
