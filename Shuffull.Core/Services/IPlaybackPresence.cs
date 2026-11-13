namespace Shuffull.Core.Services;

/// <summary>Which of a user's devices have a live connection to the playback hub right now (Shuffull#6). Held in memory.</summary>
public interface IPlaybackPresence
{
    IReadOnlySet<string> OnlineDeviceIds(string userId);
}
