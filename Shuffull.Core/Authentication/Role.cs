namespace Shuffull.Core.Authentication;

/// <summary>
/// Coarse-grained capability roles checked by <see cref="RequiresRoleAttribute"/> on MediatR requests.
/// Roles are derived from boolean flags on the <see cref="Models.Database.User"/> (see
/// <see cref="RoleMembership"/>) rather than stored as data, so granting/revoking a role is a single DB flag
/// toggle and is read live on every request (no token reissue needed). Kept intentionally small for now —
/// this is the lightweight precursor to a fuller auth model, not the fuller model itself.
/// </summary>
public enum Role
{
    /// <summary>May edit shared song metadata (name, artists, tags, BPM, energy) to fix mislabels.</summary>
    Curator = 0,
}
