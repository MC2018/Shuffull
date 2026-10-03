using FluentValidation;

namespace Shuffull.Core.Features.Songs.GetSongPage;

public class GetSongPageValidator : AbstractValidator<GetSongPageQuery>
{
    public GetSongPageValidator()
    {
        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(0).WithMessage("Page index must be zero or greater.");
    }
}
