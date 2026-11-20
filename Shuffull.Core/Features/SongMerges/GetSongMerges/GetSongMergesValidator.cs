using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.SongMerges.GetSongMerges;

public class GetSongMergesValidator : AbstractValidator<GetSongMergesQuery>
{
    public GetSongMergesValidator()
    {
        RuleFor(x => x.AfterId)
            .MaximumLength(SongMerge.MaxIdLength)
            .WithMessage($"afterId is longer than {SongMerge.MaxIdLength} characters.");
    }
}
