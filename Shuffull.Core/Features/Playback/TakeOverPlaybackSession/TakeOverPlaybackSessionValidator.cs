using FluentValidation;

namespace Shuffull.Core.Features.Playback.TakeOverPlaybackSession;

public class TakeOverPlaybackSessionValidator : AbstractValidator<TakeOverPlaybackSessionCommand>
{
    public TakeOverPlaybackSessionValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.DeviceId).ValidDeviceId();

        RuleFor(x => x.ByDeviceId).ValidDeviceId("taking device id")
            .Must((command, by) => by != command.DeviceId).WithMessage("A device can't take playback over from itself.");

        RuleFor(x => x.ExpectedUpdatedAt)
            .NotNull().WithMessage("expectedUpdatedAt is required: the session's updatedAt as you read it.");
    }
}
