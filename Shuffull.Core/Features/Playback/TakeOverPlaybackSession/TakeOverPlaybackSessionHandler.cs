using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Playback.TakeOverPlaybackSession;

/// <summary>
/// One conditional UPDATE, <c>WHERE "UpdatedAt" = expected</c>. Every state write moves <c>UpdatedAt</c>, so if the
/// source wrote after the taker read it (a skip, a pause) the takeover is a 409 and the taker re-reads, instead of
/// resuming from a stale position. The position it pauses at is estimated from the row it read, which the condition
/// guarantees is still the stored one. Returns the source session as it now is.
/// </summary>
public class TakeOverPlaybackSessionHandler(ShuffullContext context, TimeProvider timeProvider, IPlaybackNotifier notifier)
    : IRequestHandler<TakeOverPlaybackSessionCommand, Result<PlaybackSessionDto>>
{
    public async Task<Result<PlaybackSessionDto>> Handle(TakeOverPlaybackSessionCommand request, CancellationToken cancellationToken)
    {
        // The validator requires both; this guard only makes that visible here.
        if (request.ExpectedUpdatedAt is not { } expectedUpdatedAt || request.ByDeviceId is not { } byDeviceId)
        {
            return Result.Error<PlaybackSessionDto>(new KindedError(ErrorKind.Invalid,
                "A takeover needs byDeviceId and expectedUpdatedAt."));
        }

        var now = PlaybackRules.Now(timeProvider);
        var expected = PlaybackRules.AsUtc(expectedUpdatedAt);
        try
        {
            var takerIsTheirs = await context.UserDevices
                .AnyAsync(d => d.UserId == request.UserId && d.DeviceId == byDeviceId, cancellationToken);
            if (!takerIsTheirs)
            {
                return Result.Error<PlaybackSessionDto>(ResultErrors.NotFound("The taking device isn't one of your checked-in devices."));
            }

            var source = await context.PlaybackSessions.AsNoTracking()
                .SingleOrDefaultAsync(s => s.UserId == request.UserId && s.DeviceId == request.DeviceId, cancellationToken);
            if (source is null)
            {
                return Result.Error<PlaybackSessionDto>(ResultErrors.NotFound("That device has no playback session."));
            }
            if (source.UpdatedAt != expected)
            {
                return Result.Error<PlaybackSessionDto>(StaleConflict());
            }

            var position = source.EstimatePosition(now);
            var updated = await context.PlaybackSessions
                .Where(s => s.UserId == request.UserId && s.DeviceId == request.DeviceId && s.UpdatedAt == expected)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.IsPlaying, false)
                    .SetProperty(s => s.PositionSeconds, position)
                    .SetProperty(s => s.PositionAt, now)
                    .SetProperty(s => s.TakenOverBy, byDeviceId)
                    .SetProperty(s => s.TakenOverAt, now)
                    .SetProperty(s => s.UpdatedAt, now), cancellationToken);
            if (updated == 0)
            {
                return Result.Error<PlaybackSessionDto>(StaleConflict());
            }

            var row = await context.PlaybackSessions.AsNoTracking()
                .SingleAsync(s => s.UserId == request.UserId && s.DeviceId == request.DeviceId, cancellationToken);
            await notifier.SessionChangedAsync(request.UserId, request.DeviceId, writerDeviceId: byDeviceId);
            return Result.Ok(PlaybackSessionDto.Create(row, now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Error<PlaybackSessionDto>(ResultErrors.FromException(ex, $"Could not take playback over. {ex.Message}"));
        }
    }

    private static Exception StaleConflict()
        => ResultErrors.Conflict("That session changed since you read it. Read it again and retry.");
}
