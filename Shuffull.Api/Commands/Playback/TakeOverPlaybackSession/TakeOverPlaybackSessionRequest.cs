namespace Shuffull.Api.Commands.Playback.TakeOverPlaybackSession;

/// <summary>Body for a takeover: the device taking over, and the source session's <c>updatedAt</c> as it read it.</summary>
public record TakeOverPlaybackSessionRequest(string? ByDeviceId, DateTime? ExpectedUpdatedAt);
