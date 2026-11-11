using System;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartScout.Infrastructure.Data;
using SmartScout.Infrastructure.Services;
using Xunit;

namespace SmartScout.UnitTests;

public class WikipediaBioEnrichmentServiceTests
{
    [Fact]
    public void ParseWikitextBio_ShouldExtractDateOfBirthHeightWeightPositionAndDraft()
    {
        // Sample wikitext for Matthew Strazel
        string sampleWikitext = @"
{{Infobox basketball biography
| name = Matthew Strazel
| position = [[Point guard]] / [[shooting guard]]
| height_m = 1.82
| weight_kg = 81
| birth_date = {{birth date and age|df=yes|2002|08|5}}
| draft_year = 2024
}}";

        var bio = WikipediaBioEnrichmentService.ParseWikitextBio(sampleWikitext);

        Assert.NotNull(bio.BirthDate);
        Assert.Equal(2002, bio.BirthDate.Value.Year);
        Assert.Equal(8, bio.BirthDate.Value.Month);
        Assert.Equal(5, bio.BirthDate.Value.Day);

        Assert.Equal(182, bio.HeightCm);
        Assert.Equal("6-0", bio.HeightStr);
        Assert.Equal(81, bio.WeightKg);
        Assert.Equal(179, bio.WeightPounds);
        Assert.Equal("PG/SG", bio.Position);
        Assert.Equal(2024, bio.DraftYear);
    }

    [Fact]
    public void ParseWikitextBio_ShouldExtractImperialMeasurementsAndDraftPicks()
    {
        // Sample wikitext for Edy Tavares
        string sampleWikitext = @"
{{Infobox basketball biography
| name = Edy Tavares
| position = [[Center (basketball)|Center]]
| height_m = 2.22
| weight_lb = 275
| birth_date = {{Birth date and age|1992|3|22}}
| draft_year = 2014
| draft_round = 2
| draft_pick = 43
}}";

        var bio = WikipediaBioEnrichmentService.ParseWikitextBio(sampleWikitext);

        Assert.NotNull(bio.BirthDate);
        Assert.Equal(1992, bio.BirthDate.Value.Year);
        Assert.Equal(3, bio.BirthDate.Value.Month);
        Assert.Equal(22, bio.BirthDate.Value.Day);

        Assert.Equal(222, bio.HeightCm);
        Assert.Equal(125, bio.WeightKg);
        Assert.Equal(275, bio.WeightPounds);
        Assert.Equal("C", bio.Position);
        Assert.Equal(2014, bio.DraftYear);
        Assert.Equal(43, bio.DraftPick);
    }

    [Fact]
    public void ParseWikitextBio_ShouldHandleFtInchesAndForwards()
    {
        string sampleWikitext = @"
{{Infobox basketball biography
| name = Joel Parra
| position = [[Small forward]] / [[power forward]]
| height_ft = 6
| height_in = 8
| weight_kg = 102
| birth_date = {{birth date and age|df=yes|2000|04|04}}
}}";

        var bio = WikipediaBioEnrichmentService.ParseWikitextBio(sampleWikitext);

        Assert.NotNull(bio.BirthDate);
        Assert.Equal(2000, bio.BirthDate.Value.Year);
        Assert.Equal(4, bio.BirthDate.Value.Month);
        Assert.Equal(4, bio.BirthDate.Value.Day);

        Assert.Equal(203, bio.HeightCm);
        Assert.Equal("6-8", bio.HeightStr);
        Assert.Equal(102, bio.WeightKg);
        Assert.Equal("SF/PF", bio.Position);
    }

    [Fact]
    public void ParseWikitextBio_ShouldRejectHistoricalBirthDatesAndAnomalousHeights()
    {
        // Sample wikitext for historical figure mistakenly parsed (Moses Brown 1738) or bad height
        string sampleWikitext = @"
{{Infobox person
| name = Moses Brown
| birth_date = {{birth date|1738|09|23}}
| height_ft = 2
| height_in = 0
}}";

        var bio = WikipediaBioEnrichmentService.ParseWikitextBio(sampleWikitext);

        // Guardrail: Historical birth date (< 1975) must be rejected
        Assert.Null(bio.BirthDate);

        // Guardrail: Unrealistic height (< 160 cm, e.g. 2-0 -> 61 cm) must be rejected
        Assert.Null(bio.HeightCm);
        Assert.Null(bio.HeightStr);
    }

    [Fact]
    public void ParseWikitextBio_ShouldNormalizeNationalityTemplatesToIso3()
    {
        string sampleWikitext = @"
{{Infobox basketball biography
| name = Victor Wembanyama
| nationality = {{flagu|France}} / American
| position = [[Center (basketball)|Center]] / [[power forward]]
| height_m = 2.24
| birth_date = {{birth date and age|df=yes|2004|01|04}}
}}";

        var bio = WikipediaBioEnrichmentService.ParseWikitextBio(sampleWikitext);

        Assert.Equal("FRA / USA", bio.Nationality);
        Assert.Equal(224, bio.HeightCm);
        Assert.Equal("PF/C", bio.Position);
    }

    [Fact]
    public void BasketballSeason_GetDefaultSeedSeasons_ShouldHaveExactlyOneCurrentSeason()
    {
        var seasons = SmartScout.Domain.Entities.BasketballSeason.GetDefaultSeedSeasons();

        Assert.Equal(9, seasons.Count);
        var currentSeasons = System.Linq.Enumerable.Where(seasons, s => s.IsCurrent).ToList();
        Assert.Single(currentSeasons);
        Assert.Equal("2026-27", currentSeasons[0].Label);
    }

    [Theory]
    [InlineData("Alp", "Ozbudak", "HDI Sigorta Afyon Belediye", "TUR")]
    [InlineData("Göksenin", "Köksal", "Beşiktaş Gain", "TUR")]
    [InlineData("Fatih", "Cantitiz", "Karşıyaka", "TUR")]
    [InlineData("Anthony", "Brown", "Beşiktaş Gain", null)]
    public void Player_DetectNationality_ShouldNormalizeDiacriticsForTurkishClubs(string first, string last, string team, string? expected)
    {
        var result = SmartScout.Domain.Entities.Player.DetectNationality(first, last, team);
        Assert.Equal(expected, result);
    }
}
