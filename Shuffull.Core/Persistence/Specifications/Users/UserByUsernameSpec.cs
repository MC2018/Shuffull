using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Users;

/// <summary>Locates users by username: to enforce uniqueness on creation, and to find the account to verify at login.</summary>
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
