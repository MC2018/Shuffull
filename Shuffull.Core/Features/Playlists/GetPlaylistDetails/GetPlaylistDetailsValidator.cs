using FluentValidation;

namespace Shuffull.Core.Features.Playlists.GetPlaylistDetails;

public class GetPlaylistDetailsValidator : AbstractValidator<GetPlaylistDetailsQuery>
{
    public GetPlaylistDetailsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.PlaylistId)
            .NotEmpty().WithMessage("Playlist ID is required.");
    }
}
