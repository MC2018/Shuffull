using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.GenreJams.DeleteGenreJam;

public class DeleteGenreJamValidator : AbstractValidator<DeleteGenreJamCommand>
{
    public DeleteGenreJamValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        RuleFor(x => x.GenreJamId)
            .NotEmpty().WithMessage("Genre Jam id is required.")
            .MaximumLength(GenreJam.MaxIdLength).WithMessage($"Genre Jam id is longer than {GenreJam.MaxIdLength} characters.")
            .Must(id => !id.Contains('\0')).WithMessage("Genre Jam id can't contain a NUL character.");
    }
}
