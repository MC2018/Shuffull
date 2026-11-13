using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.Playback.AckPlaybackCommand;

public class AckPlaybackCommandValidator : AbstractValidator<AckPlaybackCommandCommand>
{
    public AckPlaybackCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.DeviceId).ValidDeviceId();

        RuleFor(x => x.CommandId)
            .NotEmpty().WithMessage("A command id is required.")
            .MaximumLength(PlaybackSession.MaxCommandIdLength).WithMessage($"The command id is longer than {PlaybackSession.MaxCommandIdLength} characters.")
            .Must(PlaybackRules.NoControlCharacters).WithMessage("The command id can't contain control characters.");
    }
}
