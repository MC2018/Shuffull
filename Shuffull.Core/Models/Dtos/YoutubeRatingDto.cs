namespace Shuffull.Core.Models.Dtos;

/// <summary>
/// One like-parity row as the funnel polls it. <paramref name="Rating"/> is the YouTube Data API <c>videos.rate</c>
/// value (<see cref="Features.YoutubeRatings.YoutubeRatingApiValues"/>), so the funnel can pass it straight through.
/// </summary>
public record YoutubeRatingDto(string RatingRequestId, string SongId, string VideoId, string Rating);
