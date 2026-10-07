using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.GetLibrarySongs;

/// <summary>
/// Producer-facing listing of EVERY song in the library, keyset-paged by <c>SongId</c>, so the funnel's duplicate
/// registry can cover songs it never exported itself.
///
/// <para>Why it exists: the funnel's duplicate gates (exact key, acoustic fingerprint, title+artist) only searched
/// its own exported <c>Videos</c>. Songs that reached the site any other way — the whole pre-funnel library, manual
/// imports — were invisible to them, so a second upload of one of those songs went straight through. The funnel
/// diffs this listing against its registry and fingerprints whatever is missing.</para>
///
/// <para>A listing rather than a change feed: the funnel derives its work from current state each pass, so a
/// deleted song simply drops out and there is no watermark to lose.</para>
/// </summary>
/// <param name="AfterSongId">Exclusive keyset cursor; null/empty for the first page.</param>
public record GetLibrarySongsQuery(string? AfterSongId, int Limit) : IRequest<Result<LibrarySongsPage>>;

/// <summary>
/// One library song: its identity, the text the duplicate matcher compares, and where its audio lives
/// (<c>/music/{FileHash}{FileExtension}</c>) so the producer can fingerprint it.
/// </summary>
public record LibrarySong(
    string SongId,
    string? ExternalSongId,
    string Name,
    IReadOnlyList<string> Artists,
    string FileHash,
    string FileExtension);

/// <summary><see cref="NextAfterSongId"/> is null on the last page.</summary>
public record LibrarySongsPage(IReadOnlyList<LibrarySong> Songs, string? NextAfterSongId);
