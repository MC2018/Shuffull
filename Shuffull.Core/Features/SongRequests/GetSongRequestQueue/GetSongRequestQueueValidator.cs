using FluentValidation;

namespace Shuffull.Core.Features.SongRequests.GetSongRequestQueue;

public class GetSongRequestQueueValidator : AbstractValidator<GetSongRequestQueueQuery>
{
    public GetSongRequestQueueValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid song request status.");
    }
}
