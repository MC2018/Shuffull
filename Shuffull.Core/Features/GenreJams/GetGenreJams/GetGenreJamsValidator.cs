using FluentValidation;

namespace Shuffull.Core.Features.GenreJams.GetGenreJams;

public class GetGenreJamsValidator : AbstractValidator<GetGenreJamsQuery>
{
    public GetGenreJamsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");
    }
}
