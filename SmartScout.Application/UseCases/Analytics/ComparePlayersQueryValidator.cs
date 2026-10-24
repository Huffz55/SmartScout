using FluentValidation;

namespace SmartScout.Application.UseCases.Analytics;

public class ComparePlayersQueryValidator : AbstractValidator<ComparePlayersQuery>
{
    public ComparePlayersQueryValidator()
    {
        RuleFor(x => x.PlayerAId)
            .NotEmpty()
            .WithMessage("PlayerAId is required.");

        RuleFor(x => x.SeasonA)
            .InclusiveBetween(1946, 2030)
            .WithMessage("SeasonA must be a valid NBA season between 1946 and 2030.");

        RuleFor(x => x.PlayerBId)
            .NotEmpty()
            .WithMessage("PlayerBId is required.");

        RuleFor(x => x.SeasonB)
            .InclusiveBetween(1946, 2030)
            .WithMessage("SeasonB must be a valid NBA season between 1946 and 2030.");
    }
}
