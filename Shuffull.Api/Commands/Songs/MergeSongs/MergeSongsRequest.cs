namespace Shuffull.Api.Commands.Songs.MergeSongs;

/// <summary>Body for a curator's merge: <see cref="LoserSongId"/> is folded into <see cref="SurvivorSongId"/> and deleted.</summary>
public record MergeSongsRequest(string? SurvivorSongId, string? LoserSongId);
