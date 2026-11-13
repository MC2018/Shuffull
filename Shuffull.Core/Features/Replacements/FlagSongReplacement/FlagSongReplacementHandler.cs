using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Core.Tools;

namespace Shuffull.Core.Features.Replacements.FlagSongReplacement;

/// <summary>
/// A flag is global per song: while one is open (Pending or InProgress) another adds nothing. Resolution happens when a
/// replacement import lands (SongImportService.ReplaceInDbAsync marks the request Completed), after which the song can
/// be flagged again.
///
/// <para>No catch: a database fault stays an unhandled 500, as it was before this moved out of the controller.</para>
/// </summary>
public class FlagSongReplacementHandler(ShuffullContext context, TimeProvider timeProvider)
    : IRequestHandler<FlagSongReplacementCommand, Result<FlagSongReplacementResponse>>
{
    public async Task<Result<FlagSongReplacementResponse>> Handle(FlagSongReplacementCommand request, CancellationToken cancellationToken)
    {
        var song = await context.Songs.FirstOrDefaultAsync(s => s.SongId == request.SongId, cancellationToken);
        if (song is null)
        {
            return Result.Error<FlagSongReplacementResponse>(ResultErrors.NotFound("Song not found."));
        }

        var alreadyOpen = await context.SongReplacements
            .AnyAsync(r => r.SongId == request.SongId && r.Status != SongReplacementStatus.Completed, cancellationToken);
        if (alreadyOpen)
        {
            return Result.Ok(new FlagSongReplacementResponse(Queued: false));
        }

        context.SongReplacements.Add(new SongReplacement
        {
            SongReplacementId = IdGenerator.Generate(),
            SongId = request.SongId,
            Status = SongReplacementStatus.Pending,
            Note = request.Note,
            // Snapshot the source id now, before a replacement overwrites Song.ExternalSongId.
            OriginalExternalSongId = song.ExternalSongId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        await context.SaveChangesAsync(cancellationToken);

        return Result.Ok(new FlagSongReplacementResponse(Queued: true));
    }
}
