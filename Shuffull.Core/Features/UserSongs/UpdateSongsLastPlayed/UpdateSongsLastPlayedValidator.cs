using FluentValidation;

namespace Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;

public class UpdateSongsLastPlayedValidator : AbstractValidator<UpdateSongsLastPlayedCommand>
{
    public UpdateSongsLastPlayedValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.Updates)
            .NotEmpty().WithMessage("No data was provided.");

        RuleForEach(x => x.Updates).ChildRules(update =>
        {
            update.RuleFor(u => u.SongId)
                .NotEmpty().WithMessage("Song ID is required.");
        });
    }
}
