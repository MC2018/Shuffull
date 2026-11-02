using Shuffull.Core.Authentication;
using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Tests.Authentication;

/// <summary>
/// Pins the stored server hash. Every account's <see cref="User.ServerHash"/> verifies only under the exact
/// argon2 settings, salt and encoding it was made with, so these values must never change. They were computed
/// before Shuffull.Shared's Hasher (and with it the argon2 package Core used) moved into Core.
/// </summary>
public class ServerPasswordHashTest
{
    private const string ClientHash = "bfe522df2c9dce7e7fe8cfe6d9caafec";

    [Fact]
    public void Verify_AcceptsAPinnedHashUnderTheLegacySharedSalt()
    {
        var user = new User { UserId = "u", Username = "u", ServerHash = "23a1c01ab3fa96d9d8b50c03ab7afb49" };

        Assert.True(ServerPasswordHash.Verify(user, ClientHash));
    }

    [Fact]
    public void Verify_AcceptsAPinnedHashUnderAPerUserSalt()
    {
        var user = new User
        {
            UserId = "u",
            Username = "u",
            ServerHashSalt = "00112233445566778899aabbccddeeff",
            ServerHash = "d53ec4714cba793a6486391545372bcf",
        };

        Assert.True(ServerPasswordHash.Verify(user, ClientHash));
    }
}
