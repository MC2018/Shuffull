using FluentValidation;

namespace Shuffull.Core.Features.Songs.KeepSongs;

public class KeepSongsValidator : AbstractValidator<KeepSongsCommand>
{
    public KeepSongsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.SongIds)
            .NotNull().WithMessage("Song IDs are required.");
    }
}
