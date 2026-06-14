using FluentValidation;

namespace Shuffull.Core.Features.Tags.AddTagToSong;

public class AddTagToSongValidator : AbstractValidator<AddTagToSongCommand>
{
    public AddTagToSongValidator()
    {
        RuleFor(x => x.SongId)
            .NotEmpty().WithMessage("Song ID is required.");

        RuleFor(x => x.TagId)
            .NotEmpty().WithMessage("Tag ID is required.");
    }
}
