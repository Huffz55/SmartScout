using System;
using SmartScout.Application.UseCases.SeasonStats;
using Xunit;

namespace SmartScout.UnitTests;

public class PlayerMergeHelperTests
{
    [Theory]
    [InlineData("PG/SG", "G", "PG/SG")]
    [InlineData("PG/SG", "PG", "PG/SG")]
    [InlineData("PF", "F", "PF")]
    [InlineData("C", "F", "C")]
    [InlineData("C", "F-C", "C")]
    [InlineData("G", "PG", "PG")]
    [InlineData("F", "PF", "PF")]
    [InlineData("C", "PF/C", "PF/C")]
    [InlineData("G-F", "SG", "SG")]
    [InlineData(null, "SF", "SF")]
    public void ResolvePosition_PreservesSpecificPositionsAndUpgradesCoarseOnes(
        string? existing, string? incoming, string expected)
    {
        var result = PlayerMergeHelper.ResolvePosition(existing, incoming);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("USA", "INTL", "USA")]
    [InlineData("TUR", null, "TUR")]
    [InlineData("SRB", "US", "SRB")]
    [InlineData("INTL", "ESP", "ESP")]
    [InlineData(null, "FRA", "FRA")]
    public void ResolveNationality_NeverOverwritesRealNationalityWithPlaceholder(
        string? existing, string? incoming, string expected)
    {
        var result = PlayerMergeHelper.ResolveNationality(existing, incoming, "Test", "Player", "Test Team");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveBirthDate_PreservesExistingDateOverAgeEstimate()
    {
        var exact = new DateTime(1995, 5, 4, 0, 0, 0, DateTimeKind.Utc);
        var estimated = new DateTime(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(exact, PlayerMergeHelper.ResolveBirthDate(exact, estimated));
        Assert.Equal(estimated, PlayerMergeHelper.ResolveBirthDate(null, estimated));
    }
}
