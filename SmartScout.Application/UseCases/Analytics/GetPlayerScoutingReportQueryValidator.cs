using FluentValidation;
using System;

namespace SmartScout.Application.UseCases.Analytics;

public class GetPlayerScoutingReportQueryValidator : AbstractValidator<GetPlayerScoutingReportQuery>
{
    public GetPlayerScoutingReportQueryValidator()
    {
        RuleFor(x => x.PlayerId)
            .NotEmpty()
            .WithMessage("PlayerId is required.");

        RuleFor(x => x.Season)
            .InclusiveBetween(1946, 2030)
            .WithMessage("Season must be a valid NBA season between 1946 and 2030.");
    }
}
