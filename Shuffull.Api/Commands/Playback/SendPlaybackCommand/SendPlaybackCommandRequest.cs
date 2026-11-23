namespace Shuffull.Api.Commands.Playback.SendPlaybackCommand;

/// <summary>Body for a remote command: the sender's id for it, its kind, and for a seek, where to.</summary>
public record SendPlaybackCommandRequest(string? CommandId, string? Kind, double? SeekSeconds);
