using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.Songs.MergeSongs;

public class MergeSongsValidator : AbstractValidator<MergeSongsCommand>
{
    public MergeSongsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.SurvivorSongId)
            .NotEmpty().WithMessage("survivorSongId is required.")
            .MaximumLength(SongDuplicateReport.MaxIdLength).WithMessage($"survivorSongId is longer than {SongDuplicateReport.MaxIdLength} characters.");

        RuleFor(x => x.LoserSongId)
            .NotEmpty().WithMessage("loserSongId is required.")
            .MaximumLength(SongDuplicateReport.MaxIdLength).WithMessage($"loserSongId is longer than {SongDuplicateReport.MaxIdLength} characters.")
            .NotEqual(x => x.SurvivorSongId).WithMessage("loserSongId must be a different song from survivorSongId.");
    }
}
