using SmartScout.Infrastructure.Services.Scraping;
using Xunit;

namespace SmartScout.UnitTests;

public class BasketballReferenceUrlBuilderTests
{
    private readonly BasketballReferenceUrlBuilder _builder = new();

    [Theory]
    [InlineData("LeBron", "James", 1, "j/jamesle01.html", "https://www.basketball-reference.com/players/j/jamesle01.html")]
    [InlineData("Luka", "Dončić", 1, "d/doncilu01.html", "https://www.basketball-reference.com/players/d/doncilu01.html")]
    [InlineData("Karl-Anthony", "Towns", 1, "t/townska01.html", "https://www.basketball-reference.com/players/t/townska01.html")]
    [InlineData("Jaren", "Jackson Jr.", 1, "j/jacksja01.html", "https://www.basketball-reference.com/players/j/jacksja01.html")]
    [InlineData("Marcus", "Morris Sr.", 1, "m/morrima01.html", "https://www.basketball-reference.com/players/m/morrima01.html")]
    [InlineData("Shaquille", "O'Neal", 1, "o/onealsh01.html", "https://www.basketball-reference.com/players/o/onealsh01.html")]
    [InlineData("Ty", "Lue", 1, "l/luety01.html", "https://www.basketball-reference.com/players/l/luety01.html")]
    [InlineData("Nikola", "Jokić", 1, "j/jokicni01.html", "https://www.basketball-reference.com/players/j/jokicni01.html")]
    public void GeneratePlayerUrl_ShouldGenerateAccurateSlugs(
        string firstName, string lastName, int suffixIndex, string expectedSlug, string expectedUrl)
    {
        var result = _builder.GeneratePlayerUrl(firstName, lastName, suffixIndex);

        Assert.NotNull(result);
        Assert.Equal(expectedSlug, result.Value.slug);
        Assert.Equal(expectedUrl, result.Value.url);
    }

    [Theory]
    [InlineData("Dončić", "Doncic")]
    [InlineData("Jokić", "Jokic")]
    [InlineData("Schröder", "Schroder")]
    [InlineData("Valančiūnas", "Valanciunas")]
    public void RemoveDiacritics_ShouldFlattenSpecialCharacters(string input, string expected)
    {
        var result = _builder.RemoveDiacritics(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Jaren Jackson Jr.", "Jaren Jackson")]
    [InlineData("Marcus Morris Sr.", "Marcus Morris")]
    [InlineData("Robert Williams III", "Robert Williams")]
    [InlineData("Otto Porter Jr.", "Otto Porter")]
    public void RemoveSuffixes_ShouldStripTrailingSuffixes(string input, string expected)
    {
        var result = _builder.RemoveSuffixes(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Luka Dončić", "Luka", "Dončić", true)]
    [InlineData("LeBron James", "LeBron", "James", true)]
    [InlineData("Stephen Curry", "Seth", "Curry", false)]
    public void IsPlayerMatch_ShouldCorrectlyMatchPlayerNames(
        string pageName, string firstName, string lastName, bool expectedMatch)
    {
        var result = _builder.IsPlayerMatch(pageName, firstName, lastName);
        Assert.Equal(expectedMatch, result);
    }
}
