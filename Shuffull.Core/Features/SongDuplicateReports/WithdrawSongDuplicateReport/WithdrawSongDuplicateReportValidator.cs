using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.SongDuplicateReports.WithdrawSongDuplicateReport;

public class WithdrawSongDuplicateReportValidator : AbstractValidator<WithdrawSongDuplicateReportCommand>
{
    public WithdrawSongDuplicateReportValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.ReportId)
            .NotEmpty().WithMessage("Report id is required.")
            .MaximumLength(SongDuplicateReport.MaxIdLength).WithMessage($"Report id is longer than {SongDuplicateReport.MaxIdLength} characters.")
            .Must(id => id?.Contains('\0') != true).WithMessage("Report id can't contain a NUL character.");
    }
}
