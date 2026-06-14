namespace Shuffull.Core.Features.UserSongs.UpdateSongsLastPlayed;

/// <summary>
/// How many play records actually moved forward. A requested timestamp older than the stored one is
/// ignored (last-played only advances), so <see cref="UpdatedCount"/> may be less than the number of
/// requested updates.
/// </summary>
public record UpdateSongsLastPlayedResponse(int UpdatedCount);
