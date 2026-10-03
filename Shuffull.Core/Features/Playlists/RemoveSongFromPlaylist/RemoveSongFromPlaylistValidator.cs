using FluentValidation;

namespace Shuffull.Core.Features.Playlists.RemoveSongFromPlaylist;

public class RemoveSongFromPlaylistValidator : AbstractValidator<RemoveSongFromPlaylistCommand>
{
    public RemoveSongFromPlaylistValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.PlaylistId)
            .NotEmpty().WithMessage("Playlist ID is required.");

        RuleFor(x => x.SongId)
            .NotEmpty().WithMessage("Song ID is required.");
    }
}
