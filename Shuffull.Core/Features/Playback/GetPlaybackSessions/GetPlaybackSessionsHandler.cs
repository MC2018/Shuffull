using MediatR;
using Microsoft.EntityFrameworkCore;
using Nut.Results;
using Shuffull.Core.Errors;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Models.Dtos;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services;

namespace Shuffull.Core.Features.Playback.GetPlaybackSessions;

/// <summary>
/// Reads the user's devices and sessions (two small queries: a user has a handful of devices) and joins them in memory.
/// A device's last activity is its last check-in or its session's last write, whichever is later.
/// </summary>
public class GetPlaybackSessionsHandler(ShuffullContext context, TimeProvider timeProvider, IPlaybackPresence presence)
    : IRequestHandler<GetPlaybackSessionsQuery, Result<GetPlaybackSessionsResponse>>
{
    public async Task<Result<GetPlaybackSessionsResponse>> Handle(GetPlaybackSessionsQuery request, CancellationToken cancellationToken)
    {
        var now = PlaybackRules.Now(timeProvider);
        var cutoff = now - PlaybackSession.RetentionPeriod;
        try
        {
            var devices = await context.UserDevices.AsNoTracking()
                .Where(d => d.UserId == request.UserId)
                .ToListAsync(cancellationToken);
            var sessions = await context.PlaybackSessions.AsNoTracking()
                .Where(s => s.UserId == request.UserId)
                .ToDictionaryAsync(s => s.DeviceId, cancellationToken);
            var online = presence.OnlineDeviceIds(request.UserId);

            var listing = devices
                .Select(device =>
                {
                    var session = sessions.GetValueOrDefault(device.DeviceId);
                    var lastActive = session is not null && session.UpdatedAt > device.LastSeenAt ? session.UpdatedAt : device.LastSeenAt;
                    lastActive = PlaybackSessionDto.Utc(lastActive);
                    return new PlaybackDeviceDto(
                        device.DeviceId,
                        device.Name,
                        device.Platform,
                        lastActive,
                        online.Contains(device.DeviceId),
                        session is null ? null : PlaybackSessionDto.Create(session, now));
                })
                .Where(d => d.LastActiveAt >= cutoff || d.Online)
                .OrderByDescending(d => d.LastActiveAt)
                .ThenBy(d => d.DeviceId, StringComparer.Ordinal)
                .ToList();

            return Result.Ok(new GetPlaybackSessionsResponse(now, listing));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Error<GetPlaybackSessionsResponse>(ResultErrors.FromException(ex, $"Could not read your devices' playback. {ex.Message}"));
        }
    }
}
