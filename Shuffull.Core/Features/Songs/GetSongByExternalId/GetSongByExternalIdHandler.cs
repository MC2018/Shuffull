using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Songs.GetSongByExternalId;

public class GetSongByExternalIdHandler(ShuffullContext context)
    : IRequestHandler<GetSongByExternalIdQuery, Result<GetSongByExternalIdResponse>>
{
    public async Task<Result<GetSongByExternalIdResponse>> Handle(GetSongByExternalIdQuery request, CancellationToken cancellationToken)
    {
        // ExternalSongId is unique in practice (the import dedups on it, and a replacement moves it to the new
        // source video), so a single hit is the expected shape. A replaced song's OLD video id no longer resolves —
        // that is correct: the song now belongs to the newer source.
        var song = await context.Songs
            .AsNoTracking()
            .Where(s => s.ExternalSongId == request.ExternalSongId)
            .Select(s => new GetSongByExternalIdResponse(
                s.SongId,
                s.Name,
                s.SongArtists.Select(sa => sa.Artist.Name).ToList(),
                s.ExternalSongId,
                s.MetadataLocked,
                s.Exploratory))
            .FirstOrDefaultAsync(cancellationToken);

        return song is null
            ? Result.Error<GetSongByExternalIdResponse>($"No song found for external id '{request.ExternalSongId}'.")
            : Result.Ok(song);
    }
}
