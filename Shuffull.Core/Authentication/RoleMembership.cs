using Shuffull.Core.Models.Database;

namespace Shuffull.Core.Authentication;

/// <summary>
/// Single source of truth for mapping a <see cref="User"/>'s boolean capability flags onto <see cref="Role"/>s.
/// Both the authorization behavior and any handler that wants to double-check membership go through here, so
/// the role↔flag mapping lives in exactly one place.
/// </summary>
public static class RoleMembership
{
    /// <summary>True when <paramref name="user"/> holds <paramref name="role"/>. A null user holds nothing.</summary>
    public static bool Has(User? user, Role role)
    {
        if (user is null)
        {
            return false;
        }

        return role switch
        {
            Role.Curator => user.IsCurator,
            _ => false,
        };
    }
}
