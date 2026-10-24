using FluentValidation;

namespace SmartScout.Application.UseCases.Players;

public class SyncPlayersCommandValidator : AbstractValidator<SyncPlayersCommand>
{
    public SyncPlayersCommandValidator()
    {
        RuleFor(x => x.Cursor)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Cursor must be greater than or equal to 0.");
    }
}
