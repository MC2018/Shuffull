namespace Shuffull.Core.Features.YoutubeRatings.AckYoutubeRatings;

/// <summary>How many rows the ack completed and how many it skipped. A stale or unknown ack counts in neither.</summary>
public record AckYoutubeRatingsResponse(int Completed, int Skipped = 0);
