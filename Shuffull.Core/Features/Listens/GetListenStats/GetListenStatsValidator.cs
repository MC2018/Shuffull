using FluentValidation;

namespace Shuffull.Core.Features.Listens.GetListenStats;

public class GetListenStatsValidator : AbstractValidator<GetListenStatsQuery>
{
    public GetListenStatsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.Rule)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("The maybe-drop rule is required.")
            .Must(r => r.SkippedUnderSeconds > 0 && r.Skips >= 1 && r.Skips <= r.OfLastPlays)
            .WithMessage("The maybe-drop rule needs a positive time and 1 <= skips <= plays looked at.");
    }
}
