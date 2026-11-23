using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetSongSizes;

/// <summary>
/// How much space each song in the caller's library takes on a device (Shuffull#150), for the app's auto-download
/// settings to show what each rule costs (shuffull-workspace#150). The library is the caller's <c>UserSongs</c>,
/// auditions included. Sizes travel only through this query: they are not on <c>Song</c>, so they never change a
/// <c>Version</c> and never reach <c>/songs/changed</c>.
/// </summary>
public record GetSongSizesQuery(string UserId) : IRequest<Result<IReadOnlyList<SongSize>>>;

/// <summary>
/// One song's download size. A song whose audio file is missing is left out of the list rather than failing the call.
/// </summary>
/// <param name="AudioBytes">Bytes of the audio file (<c>/music/{fileHash}{fileExtension}</c>).</param>
/// <param name="ArtBytes">Bytes of the album art (<c>/albumart/{fileHash}.jpg</c>); 0 when the song has none.</param>
public record SongSize(string SongId, long AudioBytes, long ArtBytes);
