using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongRequests;

/// <summary>Completes a song request when the song it asked for is linked to the user who asked.</summary>
public static class SongRequestCompletion
{
    /// <summary>
    /// Marks <paramref name="songRequestId"/> <see cref="SongRequestStatus.Completed"/> with <paramref name="songId"/>.
    /// Call it inside the transaction that links the song, so the request completes exactly when the song lands.
    ///
    /// <para>One conditional UPDATE: only a request that belongs to <paramref name="userId"/> and is still open moves. A
    /// cancelled one stays cancelled, and a Failed one completes, since the producer may keep trying after it stops
    /// reporting. Returns how many rows moved (0 or 1); a request that didn't move never fails the import, because the
    /// song itself is what the user wanted.</para>
    /// </summary>
    public static async Task<int> CompleteAsync(ShuffullContext context, string? songRequestId, string userId, string songId,
        DateTime now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(songRequestId))
        {
            return 0;
        }

        var open = SongRequestRules.Open;
        return await context.SongRequests
            .Where(r => r.SongRequestId == songRequestId && r.UserId == userId && open.Contains(r.Status))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, SongRequestStatus.Completed)
                .SetProperty(r => r.SongId, songId)
                .SetProperty(r => r.FailureReason, (string?)null)
                .SetProperty(r => r.UpdatedAt, now), cancellationToken);
    }
}
