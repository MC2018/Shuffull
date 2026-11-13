using FluentValidation;

namespace Shuffull.Core.Features.Replacements.GetSongReplacementQueue;

public class GetSongReplacementQueueValidator : AbstractValidator<GetSongReplacementQueueQuery>
{
    public GetSongReplacementQueueValidator()
    {
        // Over HTTP, model binding already answers an undefined status with a 400 before this runs.
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid song replacement status.");
    }
}
