using Microsoft.EntityFrameworkCore;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.SongMerges;

/// <summary>
/// Where a merged-away song lives now. <see cref="SongMerge"/> rows are never rewritten, so a song merged into one that
/// was later merged again is found by following the chain.
/// </summary>
public static class SongMergeAliases
{
    /// <summary>How many merges a chain may run through before the lookup gives up. Real chains are one or two long.</summary>
    public const int MaxHops = 20;

    /// <summary>
    /// The live song a re-import of a merged-away source belongs to: the survivor of the merge whose loser had this
    /// source id or this audio hash. Null when no merge matches, or its chain ends at a song that is gone.
    /// </summary>
    public static async Task<Song?> FindSurvivorOfSourceAsync(
        ShuffullContext context, string? externalSongId, string fileHash, CancellationToken cancellationToken)
    {
        var hasExternalId = !string.IsNullOrEmpty(externalSongId);
        var merges = await context.SongMerges
            .AsNoTracking()
            .Where(m => m.LoserFileHash == fileHash || (hasExternalId && m.LoserExternalSongId == externalSongId))
            .OrderByDescending(m => m.MergedAt)
            .ToListAsync(cancellationToken);

        foreach (var merge in merges)
        {
            if (await FindLiveSongAsync(context, merge.SurvivorSongId, cancellationToken) is { } survivor)
            {
                return survivor;
            }
        }
        return null;
    }

    /// <summary>
    /// <paramref name="songId"/> itself when it is live, otherwise the live song its merge chain ends at. Null when
    /// neither exists (the song was purged, not merged, or its survivor is gone too).
    /// </summary>
    public static async Task<Song?> FindLiveSongAsync(ShuffullContext context, string songId, CancellationToken cancellationToken)
    {
        var current = songId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var hop = 0; hop <= MaxHops && seen.Add(current); hop++)
        {
            var song = await context.Songs.FirstOrDefaultAsync(s => s.SongId == current, cancellationToken);
            if (song is not null)
            {
                return song;
            }

            var next = await context.SongMerges
                .AsNoTracking()
                .Where(m => m.LoserSongId == current)
                .Select(m => m.SurvivorSongId)
                .FirstOrDefaultAsync(cancellationToken);
            if (next is null)
            {
                return null;
            }
            current = next;
        }
        return null;
    }
}
