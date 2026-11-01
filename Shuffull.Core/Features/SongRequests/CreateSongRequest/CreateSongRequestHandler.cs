using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Models.Enums;
using Shuffull.Core.Persistence;
using Shuffull.Shared.Tools;

namespace Shuffull.Core.Features.SongRequests.CreateSongRequest;

/// <summary>
/// Records a song request (Shuffull#57). The request row is the user's intent, so it is written in its own transaction,
/// before and regardless of any download.
/// <list type="bullet">
/// <item>The library has the song: the request is Completed now, and the song is linked to the user and this week's
/// requests playlist in the same transaction. No producer is involved.</item>
/// <item>The user has a Failed request for it: that request goes back to Pending. This is the app's retry.</item>
/// <item>The user has another open request for it: a conflict.</item>
/// <item>Otherwise a new Pending request for the producer, unless the user already has
/// <see cref="MaxOpenPerUser"/> open.</item>
/// </list>
/// Every change to an existing request is a conditional UPDATE, so a producer's ack or a cancel that commits first
/// wins and this answers a conflict.
/// </summary>
public class CreateSongRequestHandler(ShuffullContext context, TimeProvider timeProvider, ILogger<CreateSongRequestHandler> logger)
    : IRequestHandler<CreateSongRequestCommand, Result<SongRequestDto>>
{
    /// <summary>Open requests one user may have at once, so one account can't queue the producer without bound.</summary>
    public const int MaxOpenPerUser = 50;

    public async Task<Result<SongRequestDto>> Handle(CreateSongRequestCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var open = SongRequestRules.Open;
        try
        {
            var existing = await context.SongRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == request.UserId && r.ExternalSongId == request.ExternalSongId
                                          && open.Contains(r.Status), cancellationToken);
            var song = await context.Songs
                .Where(s => s.ExternalSongId == request.ExternalSongId)
                .OrderBy(s => s.SongId)
                .FirstOrDefaultAsync(cancellationToken);

            if (song is not null)
            {
                return await CompleteNowAsync(request, existing, song, now, cancellationToken);
            }
            if (existing is null)
            {
                return await QueueAsync(request, now, cancellationToken);
            }
            if (existing.Status != SongRequestStatus.Failed)
            {
                return Result.Error<SongRequestDto>(ResultErrors.Conflict($"You already requested this song; it is {existing.Status}."));
            }

            // Retry: the same row goes back on the queue. Only Accepted may follow Pending, so a producer still holding
            // the old attempt has to claim it again; its import completes the request either way.
            var retried = await context.SongRequests
                .Where(r => r.SongRequestId == existing.SongRequestId && r.Status == SongRequestStatus.Failed)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, SongRequestStatus.Pending)
                    .SetProperty(r => r.FailureReason, (string?)null)
                    .SetProperty(r => r.UpdatedAt, now), cancellationToken);
            return retried == 0
                ? Result.Error<SongRequestDto>(ResultErrors.Conflict("This request changed while it was being retried; refresh it."))
                : Result.Ok(await ReadAsync(existing.SongRequestId, cancellationToken));
        }
        catch (Exception ex)
        {
            var error = ResultErrors.FromException(ex, $"Could not save the song request: {ex.Message}");
            // A concurrent request for the same song won the open-request index (or the weekly playlist's). Nothing of
            // this one was written; the app reads 409 as "already requested" and refreshes its list.
            return Result.Error<SongRequestDto>(error.Kind == ErrorKind.Conflict
                ? ResultErrors.Conflict("This song was already requested.")
                : error);
        }
    }

    private async Task<Result<SongRequestDto>> QueueAsync(CreateSongRequestCommand request, DateTime now, CancellationToken cancellationToken)
    {
        var open = SongRequestRules.Open;
        var openCount = await context.SongRequests
            .CountAsync(r => r.UserId == request.UserId && open.Contains(r.Status), cancellationToken);
        if (openCount >= MaxOpenPerUser)
        {
            return Result.Error<SongRequestDto>(ResultErrors.Conflict(
                $"You already have {openCount} open song requests; wait for some to finish."));
        }

        var row = NewRow(request, now);
        context.SongRequests.Add(row);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Ok(SongRequestMapping.ToDto(row));
    }

    /// <summary>
    /// The instant path, in one transaction: the Completed request and the links commit together or not at all. Linked
    /// for audition, like a downloaded request, so the song lands in the user's audition playlist for them to try.
    /// </summary>
    private async Task<Result<SongRequestDto>> CompleteNowAsync(CreateSongRequestCommand request, SongRequest? existing,
        Song song, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var row = existing ?? NewRow(request, now);
        if (existing is null)
        {
            row.Status = SongRequestStatus.Completed;
            row.SongId = song.SongId;
            context.SongRequests.Add(row);
        }

        await ImportTargets.LinkAsync(context, song, request.UserId, playlistId: null, row.TargetPlaylistName,
            importedForAudition: true, LikeStatus.Neutral, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        if (existing is not null)
        {
            var completed = await SongRequestCompletion.CompleteAsync(context, existing.SongRequestId, request.UserId,
                song.SongId, now, logger, cancellationToken);
            if (completed == 0)
            {
                // Cancelled (or completed) since it was read. Disposing the transaction rolls the links back.
                return Result.Error<SongRequestDto>(ResultErrors.Conflict("This request changed while it was being completed; refresh it."));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok(existing is null ? SongRequestMapping.ToDto(row) : await ReadAsync(existing.SongRequestId, cancellationToken));
    }

    private static SongRequest NewRow(CreateSongRequestCommand request, DateTime now) => new()
    {
        SongRequestId = IdGenerator.Generate(),
        UserId = request.UserId,
        ExternalSongId = request.ExternalSongId,
        Title = request.Title.Trim(),
        ArtistsJson = JsonSerializer.Serialize((request.Artists ?? []).Select(a => a.Trim()).ToList()),
        DurationSeconds = request.DurationSeconds,
        ThumbnailUrl = string.IsNullOrWhiteSpace(request.ThumbnailUrl) ? null : request.ThumbnailUrl.Trim(),
        TargetPlaylistName = SongRequestRules.PlaylistName(now),
        Status = SongRequestStatus.Pending,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private async Task<SongRequestDto> ReadAsync(string songRequestId, CancellationToken cancellationToken) =>
        SongRequestMapping.ToDto(await context.SongRequests.AsNoTracking()
            .SingleAsync(r => r.SongRequestId == songRequestId, cancellationToken));
}
