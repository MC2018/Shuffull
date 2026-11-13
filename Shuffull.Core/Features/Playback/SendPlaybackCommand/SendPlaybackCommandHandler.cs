using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;

namespace Shuffull.Core.Features.Playback.SendPlaybackCommand;

/// <summary>
/// One UPDATE of the target's command fields, scoped to the user. It leaves <c>UpdatedAt</c> alone: a command is not a
/// change of the device's state, and moving it would make a takeover in flight fail for no reason. Signals every one of
/// the user's connections, the sender's included, since the server can't tell which connection sent it.
/// </summary>
public class SendPlaybackCommandHandler(ShuffullContext context, TimeProvider timeProvider, IPlaybackNotifier notifier)
    : IRequestHandler<SendPlaybackCommandCommand, Result<PlaybackSessionDto>>
{
    public async Task<Result<PlaybackSessionDto>> Handle(SendPlaybackCommandCommand request, CancellationToken cancellationToken)
    {
        var now = PlaybackRules.Now(timeProvider);
        var seek = request.SeekSeconds;
        try
        {
            var updated = await context.PlaybackSessions
                .Where(s => s.UserId == request.UserId && s.DeviceId == request.DeviceId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Command, request.Kind)
                    .SetProperty(s => s.CommandId, request.CommandId)
                    .SetProperty(s => s.CommandSeekSeconds, seek)
                    .SetProperty(s => s.CommandAt, now), cancellationToken);
            if (updated == 0)
            {
                return Result.Error<PlaybackSessionDto>(ResultErrors.NotFound("That device has no playback session."));
            }

            var row = await context.PlaybackSessions.AsNoTracking()
                .SingleAsync(s => s.UserId == request.UserId && s.DeviceId == request.DeviceId, cancellationToken);
            await notifier.SessionChangedAsync(request.UserId, request.DeviceId, writerDeviceId: null);
            return Result.Ok(PlaybackSessionDto.Create(row, now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Error<PlaybackSessionDto>(ResultErrors.FromException(ex, $"Could not send the command. {ex.Message}"));
        }
    }
}
