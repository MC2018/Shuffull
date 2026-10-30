using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Features.Songs.GetPendingTagSongs;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Songs.HoldTagSongs;

/// <summary>
/// Restarts a held song's backoff: its attempt count goes back to 0 and it is due again within
/// <see cref="GetPendingTagSongsHandler.FirstRetryDelay"/>, under one funnel cycle. Resetting the count matters as much
/// as the delay. A song held across three batches is offered for days, and counting each hold would trip
/// UntaggedKeptSongsCheck, which is for songs the funnel fails on. A batch that never answers is the funnel's
/// AiQueueCheck's to report, and a batch the funnel gives up on is run live rather than held again.
///
/// <para>Conditional, like the rating ack: only a song still backing off from a hand-out is touched. One whose tags
/// were written since (ApplySongTags clears <c>NextTagAttemptAt</c>) is left alone, and a due time is only ever
/// brought forward, never pushed back. So a late or repeated report can't delay anything.</para>
/// </summary>
public class HoldTagSongsHandler(ShuffullContext context, TimeProvider timeProvider)
    : IRequestHandler<HoldTagSongsCommand, Result>
{
    /// <summary>The most ids one report may carry, matching the queue's page cap.</summary>
    public const int MaxSongIds = 200;

    public async Task<Result> Handle(HoldTagSongsCommand request, CancellationToken cancellationToken)
    {
        var ids = request.SongIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Ok();
        }
        if (ids.Count > MaxSongIds)
        {
            return Result.Error(new KindedError(ErrorKind.Invalid, $"At most {MaxSongIds} song ids per hold report."));
        }

        var dueBy = timeProvider.GetUtcNow().UtcDateTime + GetPendingTagSongsHandler.FirstRetryDelay;
        try
        {
            await context.Songs
                .Where(s => ids.Contains(s.SongId) && s.NextTagAttemptAt != null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.TagAttempts, 0)
                    .SetProperty(s => s.NextTagAttemptAt, s => s.NextTagAttemptAt < dueBy ? s.NextTagAttemptAt : dueBy),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Error(ResultErrors.FromException(ex, $"Could not record the tagging holds: {ex.Message}"));
        }

        return Result.Ok();
    }
}
