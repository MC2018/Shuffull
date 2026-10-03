using FluentValidation;

namespace Shuffull.Core.Features.Songs.GetSong;

public class GetSongValidator : AbstractValidator<GetSongQuery>
{
    public GetSongValidator()
    {
        RuleFor(x => x.SongId)
            .NotEmpty().WithMessage("Song ID is required.");
    }
}
