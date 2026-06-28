using Shuffull.Core.Authentication;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Authentication;

public class RoleMembershipTest
{
    private static User UserWith(bool isCurator) => new()
    {
        UserId = "u1",
        Username = "u1",
        Version = DateTime.UtcNow,
        ServerHash = "hash",
        IsCurator = isCurator,
    };

    [Fact]
    public void Has_NullUser_IsFalse()
    {
        Assert.False(RoleMembership.Has(null, Role.Curator));
    }

    [Fact]
    public void Has_CuratorFlagSet_GrantsCuratorRole()
    {
        Assert.True(RoleMembership.Has(UserWith(isCurator: true), Role.Curator));
    }

    [Fact]
    public void Has_CuratorFlagUnset_DeniesCuratorRole()
    {
        Assert.False(RoleMembership.Has(UserWith(isCurator: false), Role.Curator));
    }
}
