namespace Shuffull.Core.Features.Songs.HoldTagSongs;

/// <summary>
/// <see cref="Held"/> is how many of the reported songs had their backoff restarted. A song tagged since its hand-out,
/// or an id the site doesn't have, is left alone and not counted.
/// </summary>
public record HoldTagSongsResponse(int Held);
