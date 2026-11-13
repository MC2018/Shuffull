namespace Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;

/// <summary>
/// How many play records moved forward or were created. A timestamp older than the stored one (last-played only
/// advances) and an id that was skipped (no such song, or one the play may not add) are not counted, so
/// <see cref="UpdatedCount"/> may be less than the number of requested updates.
/// </summary>
public record UpdateSongsLastPlayedResponse(int UpdatedCount);
