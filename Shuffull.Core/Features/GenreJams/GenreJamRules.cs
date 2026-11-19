using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.GenreJams;

public static class GenreJamRules
{
    /// <summary>
    /// A Genre Jam id as the app sends it in the URL: present, no longer than the column, and with no NUL, which
    /// Postgres text can't hold (the write would fail as a server error instead of a clear 400).
    /// </summary>
    public static IRuleBuilderOptions<T, string> MustBeGenreJamId<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Genre Jam id is required.")
            .MaximumLength(GenreJam.MaxIdLength).WithMessage($"Genre Jam id is longer than {GenreJam.MaxIdLength} characters.")
            .Must(id => !id.Contains('\0')).WithMessage("Genre Jam id can't contain a NUL character.");
}
