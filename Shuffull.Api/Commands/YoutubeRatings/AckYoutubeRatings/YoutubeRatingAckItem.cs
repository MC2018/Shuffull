using Shuffull.Core.Features.YoutubeRatings;

namespace Shuffull.Api.Commands.YoutubeRatings.AckYoutubeRatings;

/// <summary>One producer report. <see cref="Outcome"/> is one of <see cref="YoutubeRatingOutcomes"/>; null means applied.</summary>
public record YoutubeRatingAckItem(string RatingRequestId, string Rating, string? Outcome = null);
