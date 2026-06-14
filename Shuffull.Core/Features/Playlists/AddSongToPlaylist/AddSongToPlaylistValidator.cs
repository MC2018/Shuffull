using FluentValidation;

namespace Shuffull.Core.Features.Playlists.AddSongToPlaylist;

public class AddSongToPlaylistValidator : AbstractValidator<AddSongToPlaylistCommand>
{
    public AddSongToPlaylistValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.PlaylistId)
            .NotEmpty().WithMessage("Playlist ID is required.");

        RuleFor(x => x.SongId)
            .NotEmpty().WithMessage("Song ID is required.");
    }
}
