using FluentValidation;

namespace Shuffull.Core.Features.SongMerges.GetSongMerges;

public class GetSongMergesValidator : AbstractValidator<GetSongMergesQuery>
{
    public GetSongMergesValidator()
    {
        RuleFor(x => x.AfterId)
            .MaximumLength(Models.Database.SongDuplicateReport.MaxIdLength)
            .WithMessage($"afterId is longer than {Models.Database.SongDuplicateReport.MaxIdLength} characters.");
    }
}
