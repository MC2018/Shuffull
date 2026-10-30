using FluentValidation;

namespace Shuffull.Core.Features.Songs.HoldTagSongs;

public class HoldTagSongsValidator : AbstractValidator<HoldTagSongsCommand>
{
    public HoldTagSongsValidator()
    {
        RuleFor(x => x.SongIds)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Song IDs are required.")
            .Must(ids => ids.Count <= HoldTagSongsHandler.MaxSongIds)
            .WithMessage($"At most {HoldTagSongsHandler.MaxSongIds} song ids per hold report.");
    }
}
