namespace Shuffull.Core.Services;

/// <summary>
/// The doorbell for playback sessions (Shuffull#6): after a session row changes, tells the user's other connected
/// devices which device's row it was, so they re-read it. It carries no state. The row is the truth, and a device whose
/// connection is down catches up on its next sync, so a lost signal costs latency, never data.
/// Implemented over SignalR in Shuffull.Api.
/// </summary>
public interface IPlaybackNotifier
{
    /// <summary>
    /// Sends "session changed: <paramref name="deviceId"/>" to every connection of <paramref name="userId"/> except
    /// those of <paramref name="writerDeviceId"/>, the device that made the change (null = nobody is left out).
    /// Never throws: a failed signal is logged, and the write it follows has already committed.
    /// </summary>
    Task SessionChangedAsync(string userId, string deviceId, string? writerDeviceId);
}
