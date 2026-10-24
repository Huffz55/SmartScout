using FluentValidation;

namespace SmartScout.Application.UseCases.Analytics;

public class SmartScoutSearchQueryValidator : AbstractValidator<SmartScoutSearchQuery>
{
    public SmartScoutSearchQueryValidator()
    {
        RuleFor(x => x.Prompt)
            .NotEmpty()
            .WithMessage("Search prompt cannot be empty.")
            .MinimumLength(3)
            .WithMessage("Search prompt must be at least 3 characters.")
            .MaximumLength(200)
            .WithMessage("Search prompt cannot exceed 200 characters.");

        RuleFor(x => x.Season)
            .InclusiveBetween(1946, 2030)
            .When(x => x.Season.HasValue)
            .WithMessage("Season must be between 1946 and 2030.");

        RuleFor(x => x.Top)
            .InclusiveBetween(1, 50)
            .WithMessage("Top count must be between 1 and 50.");
    }
}
