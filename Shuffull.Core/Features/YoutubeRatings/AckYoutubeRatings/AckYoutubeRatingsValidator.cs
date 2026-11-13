using FluentValidation;

namespace Shuffull.Core.Features.YoutubeRatings.AckYoutubeRatings;

public class AckYoutubeRatingsValidator : AbstractValidator<AckYoutubeRatingsCommand>
{
    public AckYoutubeRatingsValidator()
    {
        RuleFor(x => x.Acks)
            .NotNull().WithMessage("Acks are required.");
    }
}
