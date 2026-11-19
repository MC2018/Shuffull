using FluentValidation;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;

namespace Shuffull.Core.Features.GenreJams.SaveGenreJam;

public class SaveGenreJamValidator : AbstractValidator<SaveGenreJamCommand>
{
    /// <summary>Most ids one filter list may hold. Far past any real jam; it only bounds the row.</summary>
    public const int MaxIdsPerList = 2000;

    /// <summary>Longest id a filter may name. App ids are 26 (ULID) or about 13 (derived artist id) characters.</summary>
    public const int MaxFilterIdLength = 128;

    public const int MinHeaviness = 1;
    public const int MaxHeaviness = 10;

    public SaveGenreJamValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        // Postgres text can't hold a NUL; the save would fail as a server error instead of a clear 400.
        RuleFor(x => x.GenreJamId)
            .NotEmpty().WithMessage("Genre Jam id is required.")
            .MaximumLength(GenreJam.MaxIdLength).WithMessage($"Genre Jam id is longer than {GenreJam.MaxIdLength} characters.")
            .Must(id => !id.Contains('\0')).WithMessage("Genre Jam id can't contain a NUL character.");

        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Name is required.")
            .MaximumLength(GenreJam.MaxNameLength).WithMessage($"Name is longer than {GenreJam.MaxNameLength} characters.")
            .Must(name => name?.Contains('\0') != true).WithMessage("Name can't contain a NUL character.");

        RuleFor(x => x.HeavinessMin)
            .InclusiveBetween(MinHeaviness, MaxHeaviness).When(x => x.HeavinessMin.HasValue)
            .WithMessage($"Heaviness must be between {MinHeaviness} and {MaxHeaviness}.");
        RuleFor(x => x.HeavinessMax)
            .InclusiveBetween(MinHeaviness, MaxHeaviness).When(x => x.HeavinessMax.HasValue)
            .WithMessage($"Heaviness must be between {MinHeaviness} and {MaxHeaviness}.");
        RuleFor(x => x)
            .Must(x => x.HeavinessMin <= x.HeavinessMax).When(x => x.HeavinessMin.HasValue && x.HeavinessMax.HasValue)
            .WithMessage("The lowest heaviness can't be above the highest.");

        RuleFor(x => x.Whitelists).Must(BeValidFilters!).When(x => x.Whitelists is not null)
            .WithMessage(FiltersMessage);
        RuleFor(x => x.Blacklists).Must(BeValidFilters!).When(x => x.Blacklists is not null)
            .WithMessage(FiltersMessage);
    }

    private static readonly string FiltersMessage =
        $"Each filter list holds at most {MaxIdsPerList} ids, each non-empty, at most {MaxFilterIdLength} characters, with no NUL.";

    private static bool BeValidFilters(GenreJamFilters filters) =>
        filters.AllLists().All(list => list.Count <= MaxIdsPerList
            && list.All(id => !string.IsNullOrEmpty(id) && id.Length <= MaxFilterIdLength && !id.Contains('\0')));
}
