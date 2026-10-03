using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;

namespace Shuffull.Api.Services;

/// <summary>
/// <see cref="IAuditionPromotionService"/> over <see cref="ShuffullContext"/>: one scoped UPDATE, committed on
/// its own, with no dependency on the AI stack. Registered as a singleton that opens its own scope per call,
/// mirroring <see cref="SongEnrichmentService"/>.
/// </summary>
public class AuditionPromotionService(IServiceProvider services) : IAuditionPromotionService
{
    private readonly IServiceProvider _services = services;

    public async Task<Result<int>> PromoteAsync(IReadOnlyList<string> songIds, CancellationToken cancellationToken = default!)
    {
        if (songIds is null || songIds.Count == 0)
        {
            return Result.Ok(0);
        }

        // Distinct so a duplicated id can't be counted twice; blanks can't match a key anyway.
        var ids = songIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Ok(0);
        }

        try
        {
            using var scope = _services.CreateScope();
            // Not `using` — the scope owns this context and disposes it. Disposing it here as well would be a
            // double-dispose of an object we don't own.
            var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();

            // Only the still-exploratory rows. Promotion is idempotent, and filtering here keeps Version — the
            // clients' sync cursor — from churning for songs that were already promoted.
            var songs = await context.Songs
                .Where(s => ids.Contains(s.SongId) && s.Exploratory)
                .ToListAsync(cancellationToken);

            if (songs.Count == 0)
            {
                return Result.Ok(0);
            }

            var now = DateTime.UtcNow;
            foreach (var song in songs)
            {
                // MetadataLocked songs are promoted too. The lock protects TAGS from being rewritten (and
                // enrichment still honours it); it does not mean "leave this in the audition pool". Leaving a
                // locked song exploratory would expose it to DeletePlaylistHandler's purge, and not deleting
                // beats not promoting.
                song.Exploratory = false;

                // TagModel is deliberately left as-is (null for an audition song): that is what keeps the song
                // matching the RetagStaleSongs work query, so a sweep can still find and tag it later.
                song.Version = now;
            }

            await context.SaveChangesAsync(cancellationToken);
            return Result.Ok(songs.Count);
        }
        catch (Exception e)
        {
            return Result.Error<int>(e);
        }
    }
}
