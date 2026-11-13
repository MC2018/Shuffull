using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Playback.AckPlaybackCommand;

/// <summary>
/// One conditional UPDATE, <c>WHERE "CommandId" = id</c>. No session is a 404; a session whose pending command is
/// another one (or none) is a 409, and the device re-reads the session to see the newer command.
/// </summary>
public class AckPlaybackCommandHandler(ShuffullContext context, TimeProvider timeProvider, IPlaybackNotifier notifier)
    : IRequestHandler<AckPlaybackCommandCommand, Result<PlaybackSessionDto>>
{
    public async Task<Result<PlaybackSessionDto>> Handle(AckPlaybackCommandCommand request, CancellationToken cancellationToken)
    {
        var now = PlaybackRules.Now(timeProvider);
        try
        {
            var cleared = await context.PlaybackSessions
                .Where(s => s.UserId == request.UserId && s.DeviceId == request.DeviceId && s.CommandId == request.CommandId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Command, (string?)null)
                    .SetProperty(s => s.CommandId, (string?)null)
                    .SetProperty(s => s.CommandSeekSeconds, (double?)null)
                    .SetProperty(s => s.CommandAt, (DateTime?)null), cancellationToken);

            var row = await context.PlaybackSessions.AsNoTracking()
                .SingleOrDefaultAsync(s => s.UserId == request.UserId && s.DeviceId == request.DeviceId, cancellationToken);
            if (row is null)
            {
                return Result.Error<PlaybackSessionDto>(ResultErrors.NotFound("That device has no playback session."));
            }
            if (cleared == 0)
            {
                return Result.Error<PlaybackSessionDto>(ResultErrors.Conflict("That command is no longer pending on this device."));
            }

            await notifier.SessionChangedAsync(request.UserId, request.DeviceId, writerDeviceId: request.DeviceId);
            return Result.Ok(PlaybackSessionDto.Create(row, now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Error<PlaybackSessionDto>(ResultErrors.FromException(ex, $"Could not ack the command. {ex.Message}"));
        }
    }
}
