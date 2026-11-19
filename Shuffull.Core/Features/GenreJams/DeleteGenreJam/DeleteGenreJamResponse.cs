namespace Shuffull.Core.Features.GenreJams.DeleteGenreJam;

/// <summary>
/// <see cref="Deleted"/> is false when the caller had no jam with this id: already deleted, never synced, or someone
/// else's. Either way it is gone for the caller, so it is not an error.
/// </summary>
public record DeleteGenreJamResponse(string GenreJamId, bool Deleted);
