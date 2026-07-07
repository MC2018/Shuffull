using FluentValidation;

namespace Shuffull.Core.Features.Playlists.DeletePlaylist;

public class DeletePlaylistValidator : AbstractValidator<DeletePlaylistCommand>
{
    public DeletePlaylistValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.PlaylistId)
            .NotEmpty().WithMessage("Playlist ID is required.");
    }
}
