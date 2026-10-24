using FluentValidation.TestHelper;
using SmartScout.Application.UseCases.Analytics;
using System;
using Xunit;

namespace SmartScout.UnitTests;

public class AnalyticsQueryValidatorTests
{
    private readonly GetSimilarPlayersQueryValidator _similarValidator = new();
    private readonly ComparePlayersQueryValidator _compareValidator = new();

    [Fact]
    public void GetSimilarPlayersQuery_InvalidPlayerId_ShouldFail()
    {
        var query = new GetSimilarPlayersQuery
        {
            PlayerId = Guid.Empty,
            Season = 2024,
            Top = 5
        };

        var result = _similarValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PlayerId);
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(2050)]
    public void GetSimilarPlayersQuery_InvalidSeason_ShouldFail(int season)
    {
        var query = new GetSimilarPlayersQuery
        {
            PlayerId = Guid.NewGuid(),
            Season = season,
            Top = 5
        };

        var result = _similarValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Season);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void GetSimilarPlayersQuery_InvalidTop_ShouldFail(int top)
    {
        var query = new GetSimilarPlayersQuery
        {
            PlayerId = Guid.NewGuid(),
            Season = 2024,
            Top = top
        };

        var result = _similarValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Top);
    }

    [Fact]
    public void ComparePlayersQuery_EmptyGuids_ShouldFail()
    {
        var query = new ComparePlayersQuery
        {
            PlayerAId = Guid.Empty,
            SeasonA = 2024,
            PlayerBId = Guid.Empty,
            SeasonB = 2024
        };

        var result = _compareValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PlayerAId);
        result.ShouldHaveValidationErrorFor(x => x.PlayerBId);
    }

    [Fact]
    public void ComparePlayersQuery_ValidModel_ShouldPass()
    {
        var query = new ComparePlayersQuery
        {
            PlayerAId = Guid.NewGuid(),
            SeasonA = 2024,
            PlayerBId = Guid.NewGuid(),
            SeasonB = 2023
        };

        var result = _compareValidator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GetPlayerScoutingReportQuery_InvalidModel_ShouldFail()
    {
        var validator = new GetPlayerScoutingReportQueryValidator();
        var query = new GetPlayerScoutingReportQuery
        {
            PlayerId = Guid.Empty,
            Season = 1800
        };

        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PlayerId);
        result.ShouldHaveValidationErrorFor(x => x.Season);
    }

    [Fact]
    public void GetPlayerScoutingReportQuery_ValidModel_ShouldPass()
    {
        var validator = new GetPlayerScoutingReportQueryValidator();
        var query = new GetPlayerScoutingReportQuery
        {
            PlayerId = Guid.NewGuid(),
            Season = 2024
        };

        var result = validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData(null)]
    public void SmartScoutSearchQuery_InvalidPrompt_ShouldFail(string? prompt)
    {
        var validator = new SmartScoutSearchQueryValidator();
        var query = new SmartScoutSearchQuery { Prompt = prompt! };

        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Prompt);
    }

    [Fact]
    public void SmartScoutSearchQuery_ValidPrompt_ShouldPass()
    {
        var validator = new SmartScoutSearchQueryValidator();
        var query = new SmartScoutSearchQuery
        {
            Prompt = "high efficiency playmaking bigs",
            Season = 2024,
            Top = 15
        };

        var result = validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
