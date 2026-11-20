using FluentValidation;

namespace Shuffull.Core.Features.SongDuplicateReports.ReportSongDuplicates;

/// <summary>Only the batch itself. Each item is checked in the handler, so one bad item is rejected on its own.</summary>
public class ReportSongDuplicatesValidator : AbstractValidator<ReportSongDuplicatesCommand>
{
    public ReportSongDuplicatesValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.Reports)
            .NotNull().WithMessage("Reports are required.")
            .Must(reports => reports is null || reports.Count <= ReportSongDuplicatesHandler.MaxBatch)
            .WithMessage($"At most {ReportSongDuplicatesHandler.MaxBatch} reports in one request.");
    }
}
