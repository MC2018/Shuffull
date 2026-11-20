using FluentValidation;

namespace Shuffull.Core.Features.GenreJams.DeleteGenreJam;

public class DeleteGenreJamValidator : AbstractValidator<DeleteGenreJamCommand>
{
    public DeleteGenreJamValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.GenreJamId).MustBeGenreJamId();
    }
}
