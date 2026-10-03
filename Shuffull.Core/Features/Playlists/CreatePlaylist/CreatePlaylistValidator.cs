using FluentValidation;

namespace Shuffull.Core.Features.Playlists.CreatePlaylist;

public class CreatePlaylistValidator : AbstractValidator<CreatePlaylistCommand>
{
    /// <summary>Maximum playlist name length, mirroring the legacy PlaylistController.Create.</summary>
    public const int MaxNameLength = 50;

    public CreatePlaylistValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is not valid.")
            .MaximumLength(MaxNameLength).WithMessage("Name is not valid.");
    }
}
