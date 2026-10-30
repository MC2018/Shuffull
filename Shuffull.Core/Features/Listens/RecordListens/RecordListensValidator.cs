using FluentValidation;

namespace Shuffull.Core.Features.Listens.RecordListens;

/// <summary>Checks the batch as a whole. Each listen is checked by the handler, so one bad item can't refuse the rest.</summary>
public class RecordListensValidator : AbstractValidator<RecordListensCommand>
{
    public RecordListensValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.Listens)
            .NotNull().WithMessage("Listens are required.");
    }
}
