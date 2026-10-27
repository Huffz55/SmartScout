using FluentValidation;

namespace SmartScout.Application.UseCases.Analytics;

public class GetSleeperPlayersQueryValidator : AbstractValidator<GetSleeperPlayersQuery>
{
    public GetSleeperPlayersQueryValidator()
    {
        RuleFor(x => x.Top)
            .InclusiveBetween(1, 100)
            .WithMessage("Top must be between 1 and 100.");

        RuleFor(x => x.Season)
            .InclusiveBetween(1950, 2100)
            .When(x => x.Season.HasValue)
            .WithMessage("Season must be a valid year.");
    }
}
