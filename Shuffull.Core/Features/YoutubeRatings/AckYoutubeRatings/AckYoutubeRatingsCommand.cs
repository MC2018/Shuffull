using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.YoutubeRatings.AckYoutubeRatings;

/// <summary>Producer: what happened to each rating it polled. See <see cref="AckYoutubeRatingsHandler"/>.</summary>
public record AckYoutubeRatingsCommand(IReadOnlyList<YoutubeRatingAck> Acks) : IRequest<Result<AckYoutubeRatingsResponse>>;

/// <summary>
/// One report, as the funnel sent it. <paramref name="Rating"/> is the <c>videos.rate</c> value it pushed;
/// <paramref name="Outcome"/> is one of <see cref="YoutubeRatingOutcomes"/>, and null means applied.
/// </summary>
public record YoutubeRatingAck(string RatingRequestId, string Rating, string? Outcome);
