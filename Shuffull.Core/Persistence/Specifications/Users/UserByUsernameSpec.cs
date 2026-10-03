using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Users;

/// <summary>Locates a user by username (used to enforce username uniqueness on creation).</summary>
public class UserByUsernameSpec : BaseSpecification<User>
{
    private readonly string _username;

    public UserByUsernameSpec(string username)
    {
        _username = username;
    }

    protected override IQueryable<User> BuildQuery(IQueryable<User> query)
        => query.Where(user => user.Username == _username);
}
