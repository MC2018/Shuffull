using FluentValidation;

namespace Shuffull.Core.Features.Playlists.GetUserPlaylists;

public class GetUserPlaylistsValidator : AbstractValidator<GetUserPlaylistsQuery>
{
    public GetUserPlaylistsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");
    }
}
