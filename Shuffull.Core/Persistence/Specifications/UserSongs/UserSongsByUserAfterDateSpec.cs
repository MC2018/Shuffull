using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.UserSongs;

/// <summary>
/// Loads a user's play records changed after a given version, ordered by version, for incremental
/// sync. Takes one row beyond <see cref="PageSize"/> so the caller can tell whether more pages
/// remain (mirrors the legacy UserSongController.GetAll's MAX_PAGE_LENGTH + 1 probe).
/// </summary>
public class UserSongsByUserAfterDateSpec : BaseSpecification<UserSong>
{
    /// <summary>Page size, mirroring the legacy UserSongController's MAX_PAGE_LENGTH.</summary>
    public const int PageSize = 500;

    private readonly string _userId;
    private readonly DateTime _afterDate;

    public UserSongsByUserAfterDateSpec(string userId, DateTime afterDate)
    {
        _userId = userId;
        _afterDate = afterDate;
    }

    protected override IQueryable<UserSong> BuildQuery(IQueryable<UserSong> query)
        => query
            .Where(userSong => userSong.UserId == _userId && userSong.Version > _afterDate)
            .OrderBy(userSong => userSong.Version)
            .Take(PageSize + 1);
}
