namespace Shuffull.Api.Commands.Songs.HoldTagSongs;

/// <summary>
/// Producer hold report body: the songs from this cycle's page that were held for an AI batch rather than tagged or
/// failed. See <see cref="Core.Features.Songs.HoldTagSongs.HoldTagSongsCommand"/>.
/// </summary>
public record HoldTagSongsRequest(IReadOnlyList<string>? SongIds);
