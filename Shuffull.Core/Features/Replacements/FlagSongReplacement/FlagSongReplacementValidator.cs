using FluentValidation;

namespace Shuffull.Core.Features.Replacements.FlagSongReplacement;

public class FlagSongReplacementValidator : AbstractValidator<FlagSongReplacementCommand>
{
    public FlagSongReplacementValidator()
    {
        RuleFor(x => x.SongId)
            .Must(id => !string.IsNullOrWhiteSpace(id)).WithMessage("SongId is required.");
    }
}
