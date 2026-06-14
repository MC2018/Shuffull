using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.UserSongs;

/// <summary>
/// Loads a user's play records for a specific set of song ids. Used both to deduplicate creations
/// and to locate the rows whose last-played timestamps are being updated. The id set is expected to
/// be bounded by the caller.
/// </summary>
public class UserSongsByUserAndSongIdsSpec : BaseSpecification<UserSong>
{
    private readonly string _userId;
    private readonly IReadOnlyCollection<string> _songIds;

    public UserSongsByUserAndSongIdsSpec(string userId, IReadOnlyCollection<string> songIds)
    {
        _userId = userId;
        _songIds = songIds;
    }

    protected override IQueryable<UserSong> BuildQuery(IQueryable<UserSong> query)
        => query.Where(userSong => userSong.UserId == _userId && _songIds.Contains(userSong.SongId));
}
