using Microsoft.AspNetCore.SignalR;
using Shuffull.Core.Services;

namespace Shuffull.Api.Hubs;

/// <summary>Sends "session changed" to a user's playback hub connections. Best effort: a failure is logged, never thrown.</summary>
public class SignalRPlaybackNotifier(IHubContext<PlaybackHub, IPlaybackClient> hub, PlaybackPresence presence, ILogger<SignalRPlaybackNotifier> logger)
    : IPlaybackNotifier
{
    public async Task SessionChangedAsync(string userId, string deviceId, string? writerDeviceId)
    {
        try
        {
            var connections = presence.ConnectionIds(userId, writerDeviceId);
            if (connections.Count > 0)
            {
                await hub.Clients.Clients(connections).SessionChanged(deviceId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not signal user {UserId}'s devices that {DeviceId}'s playback changed; they catch up on their next read.",
                userId, deviceId);
        }
    }
}
