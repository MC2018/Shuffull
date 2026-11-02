using FluentValidation;

namespace Shuffull.Core.Features.SongRequests.SearchSongRequests;

public class SearchSongRequestsValidator : AbstractValidator<SearchSongRequestsQuery>
{
    public const int MaxQueryLength = 200;
    public const int MaxLimit = 25;

    public SearchSongRequestsValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");
        RuleFor(x => x.Query)
            .Must(q => !string.IsNullOrWhiteSpace(q)).WithMessage("Search text is required.")
            .MaximumLength(MaxQueryLength).WithMessage($"Search text must be {MaxQueryLength} characters or fewer.");
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, MaxLimit).WithMessage($"Limit must be between 1 and {MaxLimit}.");
    }
}
