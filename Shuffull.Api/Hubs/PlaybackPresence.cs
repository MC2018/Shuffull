using Shuffull.Core.Services;
using System.Collections.Concurrent;

namespace Shuffull.Api.Hubs;

/// <summary>
/// Which playback hub connections are open, and whose: connection id to (user, device). In memory, so it is only right
/// for a single API instance, which is how prod runs. A restart empties it and every client reconnects.
/// </summary>
public class PlaybackPresence : IPlaybackPresence
{
    private readonly ConcurrentDictionary<string, (string UserId, string DeviceId)> _connections = new();

    public void Add(string connectionId, string userId, string deviceId) => _connections[connectionId] = (userId, deviceId);

    /// <summary>Forgets a closed connection, and says whose it was (null if it was never added).</summary>
    public (string UserId, string DeviceId)? Remove(string connectionId)
        => _connections.TryRemove(connectionId, out var owner) ? owner : null;

    public IReadOnlySet<string> OnlineDeviceIds(string userId)
        => _connections.Values.Where(c => c.UserId == userId).Select(c => c.DeviceId).ToHashSet();

    /// <summary>The user's connections, leaving out those of <paramref name="exceptDeviceId"/> when given.</summary>
    public IReadOnlyList<string> ConnectionIds(string userId, string? exceptDeviceId)
        => _connections
            .Where(c => c.Value.UserId == userId && c.Value.DeviceId != exceptDeviceId)
            .Select(c => c.Key)
            .ToList();
}
