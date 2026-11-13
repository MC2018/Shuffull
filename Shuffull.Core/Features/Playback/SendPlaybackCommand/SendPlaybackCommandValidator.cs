using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.Playback.SendPlaybackCommand;

public class SendPlaybackCommandValidator : AbstractValidator<SendPlaybackCommandCommand>
{
    public SendPlaybackCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.DeviceId).ValidDeviceId();

        RuleFor(x => x.CommandId)
            .NotEmpty().WithMessage("A command id is required.")
            .MaximumLength(PlaybackSession.MaxCommandIdLength).WithMessage($"The command id is longer than {PlaybackSession.MaxCommandIdLength} characters.")
            .Must(PlaybackRules.NoControlCharacters).WithMessage("The command id can't contain control characters.");

        RuleFor(x => x.Kind)
            .Must(k => k is not null && PlaybackCommandKinds.All.Contains(k))
            .WithMessage($"The command must be one of: {string.Join(", ", PlaybackCommandKinds.All)}.");

        RuleFor(x => x.SeekSeconds)
            .Must(s => s is { } seconds && double.IsFinite(seconds) && seconds >= 0)
            .When(x => x.Kind == PlaybackCommandKinds.Seek)
            .WithMessage("A seek needs seekSeconds: a number of seconds, 0 or more.");
        RuleFor(x => x.SeekSeconds)
            .Null()
            .When(x => x.Kind != PlaybackCommandKinds.Seek)
            .WithMessage("Only a seek takes seekSeconds.");
    }
}
