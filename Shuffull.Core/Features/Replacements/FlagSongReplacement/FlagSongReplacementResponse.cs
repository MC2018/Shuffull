namespace Shuffull.Core.Features.Replacements.FlagSongReplacement;

/// <summary><paramref name="Queued"/> is false when the song already had an open replacement, so nothing was added.</summary>
public record FlagSongReplacementResponse(bool Queued);
