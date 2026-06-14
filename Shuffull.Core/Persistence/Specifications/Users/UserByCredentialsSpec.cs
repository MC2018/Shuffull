using Shuffull.Core.Persistence.Specifications;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Persistence.Specifications.Users;

/// <summary>
/// Locates a user matching a username and an already-hashed server hash. Callers pass the hashed
/// value (never the raw password) so the comparison happens entirely in the query.
/// </summary>
public class UserByCredentialsSpec : BaseSpecification<User>
{
    private readonly string _username;
    private readonly string _serverHash;

    public UserByCredentialsSpec(string username, string serverHash)
    {
        _username = username;
        _serverHash = serverHash;
    }

    protected override IQueryable<User> BuildQuery(IQueryable<User> query)
        => query.Where(user => user.Username == _username && user.ServerHash == _serverHash);
}
