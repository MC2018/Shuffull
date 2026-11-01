using FluentValidation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Features.SongRequests.CreateSongRequest;

public class CreateSongRequestValidator : AbstractValidator<CreateSongRequestCommand>
{
    public const int MaxArtists = 20;
    public const int MaxArtistNameLength = 200;

    public CreateSongRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required.");

        // A source id is a short token (a YouTube video id is 11 of these characters, a Spotify id 22). Anything else
        // would reach the producer's downloader as an argument, so it is refused here.
        RuleFor(x => x.ExternalSongId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("ExternalSongId is required.")
            .Matches($"^[A-Za-z0-9_-]{{1,{SongRequest.MaxExternalSongIdLength}}}$")
            .WithMessage($"ExternalSongId must be 1 to {SongRequest.MaxExternalSongIdLength} letters, digits, '-' or '_'.");

        RuleFor(x => x.Title)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Title is required.")
            .Must(title => title.Trim().Length <= SongRequest.MaxTitleLength)
            .WithMessage($"Title is longer than {SongRequest.MaxTitleLength} characters.");

        RuleFor(x => x.Artists)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Artists is required (it may be empty).")
            .Must(artists => artists!.Count <= MaxArtists).WithMessage($"At most {MaxArtists} artists.")
            .Must(artists => artists!.All(a => !string.IsNullOrWhiteSpace(a) && a.Trim().Length <= MaxArtistNameLength))
            .WithMessage($"Each artist needs a name of at most {MaxArtistNameLength} characters.");

        RuleFor(x => x.DurationSeconds)
            .GreaterThanOrEqualTo(0).When(x => x.DurationSeconds is not null)
            .WithMessage("DurationSeconds can't be negative.");

        // The app shows it, so only an https link is kept.
        RuleFor(x => x.ThumbnailUrl)
            .Cascade(CascadeMode.Stop)
            .Must(url => url!.Trim().Length <= SongRequest.MaxThumbnailUrlLength)
            .WithMessage($"ThumbnailUrl is longer than {SongRequest.MaxThumbnailUrlLength} characters.")
            .Must(url => Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("ThumbnailUrl must be an absolute https URL.")
            .When(x => !string.IsNullOrWhiteSpace(x.ThumbnailUrl));
    }
}
