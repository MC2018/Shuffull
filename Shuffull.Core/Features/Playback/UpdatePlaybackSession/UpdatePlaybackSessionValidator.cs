using System.Text.Json;
using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.Playback.UpdatePlaybackSession;

public class UpdatePlaybackSessionValidator : AbstractValidator<UpdatePlaybackSessionCommand>
{
    public UpdatePlaybackSessionValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.DeviceId).ValidDeviceId();

        RuleFor(x => x.SongId)
            .MaximumLength(Listen.MaxIdLength).WithMessage($"The song id is longer than {Listen.MaxIdLength} characters.")
            .Must(PlaybackRules.NoControlCharacters).WithMessage("The song id can't contain control characters.");

        RuleFor(x => x.PositionSeconds)
            .Must(p => double.IsFinite(p) && p >= 0).WithMessage("The position must be a number of seconds, 0 or more.");

        RuleFor(x => x.Scope)
            .MaximumLength(PlaybackSession.MaxScopeLength).WithMessage($"The scope is longer than {PlaybackSession.MaxScopeLength} characters.")
            .Must(BeAJsonObject).WithMessage("The scope must be a JSON object.");

        RuleFor(x => x.History)
            .NotNull().WithMessage("History can't be null.")
            .Must(h => h is null || h.Count <= PlaybackSession.MaxHistory)
            .WithMessage($"History holds at most {PlaybackSession.MaxHistory} songs.");
        RuleForEach(x => x.History).Must(BeASongId).WithMessage(SongIdMessage);

        RuleFor(x => x.QueuedNext)
            .NotNull().WithMessage("The play-next list can't be null.")
            .Must(q => q is null || q.Count <= PlaybackSession.MaxQueuedNext)
            .WithMessage($"The play-next list holds at most {PlaybackSession.MaxQueuedNext} songs.");
        RuleForEach(x => x.QueuedNext).Must(BeASongId).WithMessage(SongIdMessage);
    }

    private static readonly string SongIdMessage =
        $"Every song id must be 1 to {Listen.MaxIdLength} characters, with no control characters.";

    private static bool BeASongId(string? id)
        => !string.IsNullOrEmpty(id) && id.Length <= Listen.MaxIdLength && PlaybackRules.NoControlCharacters(id);

    private static bool BeAJsonObject(string? scope)
    {
        if (scope is null)
        {
            return true;
        }
        try
        {
            using var document = JsonDocument.Parse(scope);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
