using FluentValidation;

namespace Shuffull.Core.Features.SongRequests.CancelSongRequest;

public class CancelSongRequestValidator : AbstractValidator<CancelSongRequestCommand>
{
    public CancelSongRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.SongRequestId)
            .NotEmpty().WithMessage("Song request ID is required.");
    }
}
