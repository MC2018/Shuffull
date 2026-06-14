using FluentValidation;

namespace Shuffull.Core.Features.Playlists.GetPlaylists;

public class GetPlaylistsValidator : AbstractValidator<GetPlaylistsQuery>
{
    public GetPlaylistsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.PlaylistIds)
            .NotEmpty().WithMessage("No playlist ids were received.");
    }
}
