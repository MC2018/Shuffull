using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.UserSongs;

/// <summary>
/// Loads a user's play records changed after a given version, ordered by version then song id, for
/// incremental sync. Takes one row beyond <see cref="PageSize"/> so the caller can tell whether more pages
/// remain (mirrors the legacy UserSongController.GetAll's MAX_PAGE_LENGTH + 1 probe).
/// <para>
/// With <c>afterId</c> (a SongId; unique within one user) the cursor is the keyset (Version, SongId), so a tie
/// of any size pages through by id (Shuffull#64). Without it the filter is <c>Version > afterDate</c> alone,
/// which is what installed apps still send.
/// </para>
/// </summary>
public class UserSongsByUserAfterDateSpec : BaseSpecification<UserSong>
{
    /// <summary>Page size, mirroring the legacy UserSongController's MAX_PAGE_LENGTH.</summary>
    public const int PageSize = 500;

    private readonly string _userId;
    private readonly DateTime _afterDate;
    private readonly string? _afterId;

    public UserSongsByUserAfterDateSpec(string userId, DateTime afterDate, string? afterId = null)
    {
        _userId = userId;
        _afterDate = afterDate;
        _afterId = string.IsNullOrEmpty(afterId) ? null : afterId;
    }

    protected override IQueryable<UserSong> BuildQuery(IQueryable<UserSong> query)
    {
        var mine = query.Where(userSong => userSong.UserId == _userId);
        return (_afterId == null
                ? mine.Where(userSong => userSong.Version > _afterDate)
                : mine.Where(userSong => userSong.Version > _afterDate
                    || (userSong.Version == _afterDate && string.Compare(userSong.SongId, _afterId) > 0)))
            .OrderBy(userSong => userSong.Version)
            .ThenBy(userSong => userSong.SongId)
            .Take(PageSize + 1);
    }
}
