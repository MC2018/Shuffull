using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Playback.UpdatePlaybackSession;

/// <summary>
/// Upserts the device's session row and stamps <c>PositionAt</c> and <c>UpdatedAt</c> with the server's clock. It never
/// writes the takeover or command fields, so a PUT that was in flight when another device took over can't erase the
/// takeover: the answer carries it back instead. Signals the user's other devices once the row is saved.
/// </summary>
public class UpdatePlaybackSessionHandler(ShuffullContext context, TimeProvider timeProvider, IPlaybackNotifier notifier)
    : IRequestHandler<UpdatePlaybackSessionCommand, Result<PlaybackSessionDto>>
{
    public async Task<Result<PlaybackSessionDto>> Handle(UpdatePlaybackSessionCommand request, CancellationToken cancellationToken)
    {
        PlaybackSession session;
        DateTime now;
        try
        {
            var checkedIn = await context.UserDevices
                .AnyAsync(d => d.UserId == request.UserId && d.DeviceId == request.DeviceId, cancellationToken);
            if (!checkedIn)
            {
                return Result.Error<PlaybackSessionDto>(ResultErrors.NotFound(
                    "This device isn't checked in. Check it in with PUT /api/v1/devices/{deviceId} first."));
            }

            // Two first writes from one device at once can both find no row; the loser's insert fails the key, and it
            // retries once as an update of the row the winner made.
            for (var attempt = 0; ; attempt++)
            {
                now = PlaybackRules.Now(timeProvider);
                var existing = await context.PlaybackSessions
                    .SingleOrDefaultAsync(s => s.UserId == request.UserId && s.DeviceId == request.DeviceId, cancellationToken);
                session = existing ?? new PlaybackSession { UserId = request.UserId, DeviceId = request.DeviceId };
                if (existing is null)
                {
                    context.PlaybackSessions.Add(session);
                }

                session.SongId = string.IsNullOrEmpty(request.SongId) ? null : request.SongId;
                session.PositionSeconds = request.PositionSeconds;
                session.PositionAt = now;
                session.IsPlaying = request.IsPlaying;
                session.Scope = request.Scope;
                session.History = [.. request.History];
                session.QueuedNext = [.. request.QueuedNext];
                session.UpdatedAt = now;

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateException) when (existing is null && attempt == 0)
                {
                    context.ChangeTracker.Clear();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Error<PlaybackSessionDto>(ResultErrors.FromException(ex, $"Could not save this device's playback. {ex.Message}"));
        }

        await notifier.SessionChangedAsync(request.UserId, request.DeviceId, writerDeviceId: request.DeviceId);
        return Result.Ok(PlaybackSessionDto.Create(session, now));
    }
}
