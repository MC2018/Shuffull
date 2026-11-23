using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.SongMerges;

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

    /// <summary>
    /// For each of <paramref name="songIds"/> that a merge took away, the song at the end of its merge chain. Ids no
    /// merge names are left out. A device can send a Keep, Like or library add for a song before it has pulled the
    /// merge that removed it (the outbox pushes before it pulls), so those handlers apply it to the song it became.
    /// </summary>
    public static async Task<Result<Dictionary<string, string>>> FollowMergesAsync(
        IUnitOfWork unitOfWork, IReadOnlyCollection<string> songIds, CancellationToken cancellationToken)
    {
        var survivors = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = songIds.Distinct().ToDictionary(id => id, id => id, StringComparer.Ordinal);
        for (var hop = 0; hop < MaxHops && pending.Count > 0; hop++)
        {
            var mergesResult = await unitOfWork.Repository<SongMerge>()
                .ListAsync(new SongMergesByLoserIdsSpec(pending.Values.Distinct().ToList()).AsNoTracking(), cancellationToken);
            if (mergesResult.IsError)
            {
                return Result.Error<Dictionary<string, string>>(mergesResult.GetError());
            }

            var next = mergesResult.Get().ToDictionary(m => m.LoserSongId, m => m.SurvivorSongId, StringComparer.Ordinal);
            var stillMerged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (sent, current) in pending)
            {
                if (next.TryGetValue(current, out var survivor))
                {
                    survivors[sent] = survivor;
                    stillMerged[sent] = survivor;
                }
            }
            pending = stillMerged;
        }
        return Result.Ok(survivors);
    }
}
