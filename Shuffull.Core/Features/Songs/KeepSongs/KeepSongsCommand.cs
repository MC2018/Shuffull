using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.KeepSongs;

/// <summary>
/// Records the user's decision to KEEP audition songs: promotes each one out of audition (clears
/// <c>Song.Exploratory</c>) and nothing else — no AI, no enrichment. Any signed-in user may call it, for songs in
/// their own library; ids the user has no <c>UserSong</c> for are reported failed and left untouched.
///
/// <para>Deliberately NOT <c>[RequiresRole]</c>. A Keep is a user decision, and re-tagging is curator/funnel work;
/// when both went through the curator-only RetagSongs, a non-curator's Keep was rejected before promotion ran, so
/// the song stayed exploratory and <c>DeletePlaylistHandler</c> could purge it. See the hub CLAUDE.md, "The
/// audition ladder".</para>
/// </summary>
public record KeepSongsCommand(string UserId, IReadOnlyList<string> SongIds) : IRequest<Result<KeepSongsResponse>>;
