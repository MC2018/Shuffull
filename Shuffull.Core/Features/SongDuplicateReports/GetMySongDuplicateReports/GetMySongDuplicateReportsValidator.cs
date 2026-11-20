using FluentValidation;

namespace Shuffull.Core.Features.SongDuplicateReports.GetMySongDuplicateReports;

public class GetMySongDuplicateReportsValidator : AbstractValidator<GetMySongDuplicateReportsQuery>
{
    public GetMySongDuplicateReportsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");
    }
}
