using FluentValidation.TestHelper;
using SmartScout.Application.UseCases.Players;
using Xunit;

namespace SmartScout.UnitTests;

public class ValidationTests
{
    private readonly GetPlayersQueryValidator _queryValidator = new();
    private readonly SyncPlayersCommandValidator _commandValidator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetPlayersQuery_InvalidPage_ShouldHaveValidationError(int page)
    {
        var model = new GetPlayersQuery { Page = page, PageSize = 20 };
        var result = _queryValidator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void GetPlayersQuery_InvalidPageSize_ShouldHaveValidationError(int pageSize)
    {
        var model = new GetPlayersQuery { Page = 1, PageSize = pageSize };
        var result = _queryValidator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void GetPlayersQuery_ValidValues_ShouldNotHaveValidationErrors()
    {
        var model = new GetPlayersQuery { Page = 2, PageSize = 50, SearchTerm = "Doncic" };
        var result = _queryValidator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void SyncPlayersCommand_NegativeCursor_ShouldHaveValidationError()
    {
        var model = new SyncPlayersCommand { Cursor = -1 };
        var result = _commandValidator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Cursor);
    }

    [Fact]
    public void SyncPlayersCommand_ZeroOrPositiveCursor_ShouldPassValidation()
    {
        var model = new SyncPlayersCommand { Cursor = 100 };
        var result = _commandValidator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
