namespace Shuffull.Core.Features.Tags.AddTagToSong;

/// <summary>
/// Result of tagging a song. <see cref="Added"/> is false when the song already carried the tag (a
/// no-op), so the operation is idempotent.
/// </summary>
public record AddTagToSongResponse(bool Added);
