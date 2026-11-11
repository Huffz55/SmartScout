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
            .Must(s => !s.HasValue || s.Value == 0 || (s.Value >= 1950 && s.Value <= 2100))
            .WithMessage("Season must be 0 (All Seasons) or a valid year between 1950 and 2100.");

        RuleFor(x => x.MaxAge)
            .Must(a => !a.HasValue || (a.Value >= 15 && a.Value <= 50))
            .WithMessage("MaxAge must be between 15 and 50 if specified.");
    }
}
