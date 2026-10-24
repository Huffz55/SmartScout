using FluentValidation;
using System;

namespace SmartScout.Application.UseCases.Analytics;

public class GetSimilarPlayersQueryValidator : AbstractValidator<GetSimilarPlayersQuery>
{
    public GetSimilarPlayersQueryValidator()
    {
        RuleFor(x => x.PlayerId)
            .NotEmpty()
            .WithMessage("PlayerId is required.");

        RuleFor(x => x.Season)
            .InclusiveBetween(1946, 2030)
            .WithMessage("Season must be a valid NBA season year between 1946 and 2030.");

        RuleFor(x => x.Top)
            .InclusiveBetween(1, 25)
            .WithMessage("Top count must be between 1 and 25.");

        RuleFor(x => x.MinGamesPlayed)
            .GreaterThanOrEqualTo(0)
            .WithMessage("MinGamesPlayed cannot be negative.");
    }
}
