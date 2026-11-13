using FluentValidation;

namespace Shuffull.Core.Features.YoutubeRatings.GetYoutubeRatingQueue;

public class GetYoutubeRatingQueueValidator : AbstractValidator<GetYoutubeRatingQueueQuery>
{
    public GetYoutubeRatingQueueValidator()
    {
        // Over HTTP, model binding already answers an undefined status with a 400 before this runs.
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid YouTube rating status.");
    }
}
