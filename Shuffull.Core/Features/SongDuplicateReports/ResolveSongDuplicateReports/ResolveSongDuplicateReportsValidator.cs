using FluentValidation;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;

namespace Shuffull.Core.Features.SongDuplicateReports.ResolveSongDuplicateReports;

public class ResolveSongDuplicateReportsValidator : AbstractValidator<ResolveSongDuplicateReportsCommand>
{
    public ResolveSongDuplicateReportsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.SongId)
            .NotEmpty().WithMessage("songId is required.")
            .MaximumLength(SongDuplicateReport.MaxIdLength).WithMessage($"songId is longer than {SongDuplicateReport.MaxIdLength} characters.");

        RuleFor(x => x.OtherSongId)
            .NotEmpty().WithMessage("otherSongId is required.")
            .MaximumLength(SongDuplicateReport.MaxIdLength).WithMessage($"otherSongId is longer than {SongDuplicateReport.MaxIdLength} characters.")
            .NotEqual(x => x.SongId).WithMessage("otherSongId must be a different song from songId.");

        RuleFor(x => x.Resolution)
            .Must(r => SongDuplicateReportWire.Parse<SongDuplicateReportStatus>(r) is { } status
                && SongDuplicateReportWire.Resolutions.Contains(status))
            .WithMessage("resolution must be \"notDuplicate\" or \"keptBoth\".");
    }
}
