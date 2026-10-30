using MediatR;
using Nut.Results;

namespace Shuffull.Core.Features.Songs.HoldTagSongs;

/// <summary>
/// The producer's report that it held these songs from the tagging queue for an AI batch: not a failure, just not
/// done yet. The funnel tags a song in up to three chained batches and needs it offered again to queue each next
/// link, so a held song goes back to the first retry delay instead of climbing the backoff ladder
/// (shuffull-workspace#36).
///
/// <para>Queue bookkeeping only, on the same two columns the queue already keeps (no queue table). A report that
/// never arrives costs nothing but time: the song stays on the ladder, which is how a funnel that predates reports
/// is served.</para>
/// </summary>
public record HoldTagSongsCommand(IReadOnlyList<string> SongIds) : IRequest<Result>;
