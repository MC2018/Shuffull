using FluentValidation;

namespace Shuffull.Core.Features.Songs.UpdateSongLyrics;

public class UpdateSongLyricsValidator : AbstractValidator<UpdateSongLyricsCommand>
{
    public UpdateSongLyricsValidator()
    {
        // One message at a time, the first that applies, as the endpoint has always answered.
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.ExternalSongId)
            .Must(id => !string.IsNullOrWhiteSpace(id)).WithMessage("ExternalSongId is required.");

        // Refuse an all-empty payload. Otherwise a producer-side bug (or a re-check that found nothing) could blank
        // lyrics the site already holds — a silent regression that is invisible until someone opens the lyrics panel.
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics) || !string.IsNullOrWhiteSpace(x.PlainLyrics) || x.Instrumental)
            .WithMessage("No lyrics content supplied; nothing to update.");
    }
}
