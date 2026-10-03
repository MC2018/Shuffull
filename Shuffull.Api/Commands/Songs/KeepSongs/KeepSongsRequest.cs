namespace Shuffull.Api.Commands.Songs.KeepSongs;

/// <summary>Body for the batched Keep: the ids of the audition songs the user decided to keep.</summary>
public record KeepSongsRequest(string[]? SongIds);
