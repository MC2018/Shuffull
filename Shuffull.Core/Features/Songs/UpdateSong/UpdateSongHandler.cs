using MediatR;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Core.Persistence.Specifications.Artists;
using Shuffull.Core.Persistence.Specifications.Songs;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.Songs.UpdateSong;

/// <summary>
/// Applies a curator's song-metadata edit: overwrites the scalar fields and fully rewrites the artist/tag join
/// sets (reusing shared master rows by name, creating any that don't exist), then bumps <c>Song.Version</c> so
/// clients re-sync the corrected song. Authorization is handled upstream by the role behavior.
/// </summary>
public class UpdateSongHandler(IUnitOfWork unitOfWork, ShuffullContext context) : IRequestHandler<UpdateSongCommand, Result<UpdateSongResponse>>
{
    public async Task<Result<UpdateSongResponse>> Handle(UpdateSongCommand request, CancellationToken cancellationToken)
    {
        // Tracked load (with joins) so the rewrite below is persisted by the change tracker.
        var songResult = await unitOfWork.Repository<Song>()
            .GetAsync(new SongWithRelationsByIdSpec(request.SongId), cancellationToken);
        if (songResult.IsError)
        {
            return Result.Error<UpdateSongResponse>(songResult.GetError().NotFoundAs($"Song '{request.SongId}' was not found."));
        }

        var song = songResult.Get();

        song.Name = request.Name;
        song.Bpm = request.Bpm;
        song.Heaviness = request.Heaviness;
        // Mark the song as curator-corrected so a future re-source won't overwrite this metadata
        // (SongImportService.ReplaceInDbAsync respects the lock).
        song.MetadataLocked = true;

        var artistsResult = await ResolveArtistsAsync(request.Artists, cancellationToken);
        if (artistsResult.IsError)
        {
            return Result.Error<UpdateSongResponse>(artistsResult.GetError());
        }

        // The tag inserts and the song's new joins commit together. UnitOfWork shares this context, so its save runs
        // inside the transaction.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tags = await ResolveTagsAsync(request.Tags, cancellationToken);

        // Replace the join sets wholesale: drop the old links, add fresh ones to the resolved masters. The
        // master Artist/Tag rows are shared, so they are never deleted here — only the song's links to them.
        unitOfWork.Repository<SongArtist>().DeleteRange(song.SongArtists?.ToList() ?? new List<SongArtist>());
        unitOfWork.Repository<SongTag>().DeleteRange(song.SongTags?.ToList() ?? new List<SongTag>());

        foreach (var artist in artistsResult.Get())
        {
            await unitOfWork.Repository<SongArtist>().AddAsync(new SongArtist
            {
                SongArtistId = IdGenerator.Generate(),
                SongId = song.SongId,
                ArtistId = artist.ArtistId,
            }, cancellationToken);
        }

        foreach (var tag in tags)
        {
            await unitOfWork.Repository<SongTag>().AddAsync(new SongTag
            {
                SongTagId = IdGenerator.Generate(),
                SongId = song.SongId,
                TagId = tag.TagId,
            }, cancellationToken);
        }

        // Stamp the sync cursor so every client pulls the corrected song on its next /songs/changed call.
        song.Version = DateTime.UtcNow;

        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsError)
        {
            return Result.Error<UpdateSongResponse>(saveResult.GetError());
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok(new UpdateSongResponse(song.SongId, song.Version));
    }

    /// <summary>
    /// Maps the requested artist names (trimmed, de-duped, order-preserving) onto master <see cref="Artist"/>
    /// rows, reusing an existing row per name or creating one. A song may legitimately have no artists.
    /// </summary>
    private async Task<Result<List<Artist>>> ResolveArtistsAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var requested = names
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingResult = await unitOfWork.Repository<Artist>()
            .ListAsync(new ArtistsByNamesSpec(requested), cancellationToken);
        if (existingResult.IsError)
        {
            return Result.Error<List<Artist>>(existingResult.GetError());
        }

        var existing = existingResult.Get();
        var resolved = new List<Artist>();

        foreach (var name in requested)
        {
            var artist = existing.FirstOrDefault(a => a.Name == name);
            if (artist is null)
            {
                artist = new Artist { ArtistId = IdGenerator.Generate(), Name = name };
                var addResult = await unitOfWork.Repository<Artist>().AddAsync(artist, cancellationToken);
                if (addResult.IsError)
                {
                    return Result.Error<List<Artist>>(addResult.GetError());
                }
                existing.Add(artist);
            }

            resolved.Add(artist);
        }

        return Result.Ok(resolved);
    }

    /// <summary>
    /// Maps the requested tags onto master <see cref="Tag"/> rows by name, creating a missing one with its requested TPH
    /// subtype via <see cref="TagFactory"/>. Names are unique across types (Shuffull#43), so an existing name is reused
    /// whatever its type: asking for "Romantic" as a Mood links the existing Theme "Romantic" rather than failing.
    /// <see cref="TagStore"/> also reuses a name another writer creates meanwhile (Shuffull#82).
    /// </summary>
    private Task<List<Tag>> ResolveTagsAsync(IReadOnlyList<SongTagEdit> tags, CancellationToken cancellationToken)
    {
        var requested = tags
            .Select(t => (Name: t.Name.Trim(), t.Type))
            .Where(t => t.Name.Length > 0)
            .Select(t => TagFactory.Create(IdGenerator.Generate(), t.Name, t.Type));
        return TagStore.GetOrCreateAsync(context, requested, cancellationToken);
    }
}
