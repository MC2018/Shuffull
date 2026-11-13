using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetDeletedSongs;

/// <summary>
/// The caller's own deleted songs, newest first, read from <see cref="Models.Database.SongTombstone"/>
/// (ShuffullApp#59). Lets a user look up a song they regret deleting: its name, artists, the mix it was in, and its
/// YouTube id. Only the caller's tombstones are ever returned, never another user's.
/// </summary>
/// <param name="UserId">The caller. Tombstones are filtered on <c>DeletedByUserId</c>.</param>
/// <param name="Before">Exclusive cursor from the previous page's <see cref="DeletedSongsPage.NextBefore"/>; null for
/// the first page. Opaque to clients.</param>
/// <param name="Limit">Page size; 0 or less means the default.</param>
public record GetDeletedSongsQuery(string UserId, string? Before, int Limit) : IRequest<Result<DeletedSongsPage>>;

/// <summary>One deleted song. <see cref="ArtistNames"/> and <see cref="PlaylistName"/> are null on tombstones written
/// before they were recorded.</summary>
public record DeletedSong(
    string SongId,
    string Name,
    string? ArtistNames,
    string? ExternalSongId,
    string? PlaylistName,
    DateTime DeletedAt);

/// <summary><see cref="NextBefore"/> is null on the last page.</summary>
public record DeletedSongsPage(IReadOnlyList<DeletedSong> Songs, string? NextBefore);
