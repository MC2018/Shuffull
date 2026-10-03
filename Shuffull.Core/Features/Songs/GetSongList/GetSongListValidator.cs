using FluentValidation;

namespace Shuffull.Core.Features.Songs.GetSongList;

public class GetSongListValidator : AbstractValidator<GetSongListQuery>
{
    /// <summary>
    /// Upper bound on how many songs may be requested in one call, mirroring the legacy
    /// SongController's MAX_PAGE_LENGTH so the batch query stays bounded.
    /// </summary>
    public const int MaxSongIds = 500;

    public GetSongListValidator()
    {
        RuleFor(x => x.SongIds)
            .NotEmpty().WithMessage("Song IDs were not provided.");

        RuleFor(x => x.SongIds)
            .Must(ids => ids is null || ids.Count <= MaxSongIds)
            .WithMessage($"Cannot provide more than {MaxSongIds} songs at one time.");
    }
}
