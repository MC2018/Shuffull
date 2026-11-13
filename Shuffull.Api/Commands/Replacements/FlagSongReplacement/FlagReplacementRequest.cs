namespace Shuffull.Api.Commands.Replacements.FlagSongReplacement;

/// <summary>Body for the app's replacement flag. The app never sends a note today.</summary>
public record FlagReplacementRequest(string SongId, string? Note);
