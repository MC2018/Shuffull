using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Shuffull.Core.Services;

namespace Shuffull.Api.Hubs;

/// <summary>What the server can call on a playback client.</summary>
public interface IPlaybackClient
{
    /// <summary>
    /// "The session of <paramref name="deviceId"/> changed." Nothing else: the client re-reads
    /// <c>GET /api/v1/playback/sessions</c> for the state.
    /// </summary>
    Task SessionChanged(string deviceId);
}

/// <summary>
/// The live channel for cross-device playback (Shuffull#6). Server to client only, and only "session changed:
/// deviceId", never state, so a missed signal costs latency and nothing else. Clients connect to <see cref="Path"/>
/// with <c>?deviceId=</c> (one of their checked-in devices) and their token; <see cref="PlaybackHubSignIn"/> checks both
/// before a connection exists, and tags the connection with the user and device it checked.
///
/// <para>A device's <c>online</c> in the listing comes from these connections, so connecting, and closing a device's
/// last connection, also signal "session changed" for that device to the user's other devices.</para>
/// </summary>
public class PlaybackHub(PlaybackPresence presence, IPlaybackNotifier notifier, ILogger<PlaybackHub> logger) : Hub<IPlaybackClient>
{
    public const string Path = "/hubs/playback";

    /// <summary>Claim on a hub connection's user naming the device it connected as.</summary>
    public const string DeviceClaim = "PlaybackDeviceId";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        var deviceId = Context.User?.FindFirstValue(DeviceClaim);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(deviceId))
        {
            // Only reachable if the hub were mapped without PlaybackHubSignIn in front of it.
            logger.LogWarning("Playback hub connection {ConnectionId} arrived without a checked user and device; closing it.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        presence.Add(Context.ConnectionId, userId, deviceId);
        await base.OnConnectedAsync();
        await notifier.SessionChangedAsync(userId, deviceId, writerDeviceId: deviceId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (presence.Remove(Context.ConnectionId) is { } owner
            && !presence.OnlineDeviceIds(owner.UserId).Contains(owner.DeviceId))
        {
            // That was the device's last connection, so it just went offline.
            await notifier.SessionChangedAsync(owner.UserId, owner.DeviceId, writerDeviceId: owner.DeviceId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
