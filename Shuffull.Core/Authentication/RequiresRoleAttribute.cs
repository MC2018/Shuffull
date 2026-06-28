namespace Shuffull.Core.Authentication;

/// <summary>
/// Marks a MediatR request (command/query) as requiring the current user to hold a given <see cref="Role"/>.
/// Enforced centrally by <see cref="Behaviors.RoleAuthorizationBehavior{TRequest,TResponse}"/> in the pipeline,
/// so individual handlers stay free of authorization boilerplate (mirrors the Sociallite policy attribute, but
/// role-based rather than resource-scoped). A request without this attribute is unrestricted (auth-wise).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequiresRoleAttribute : Attribute
{
    public Role Role { get; }

    public RequiresRoleAttribute(Role role)
    {
        Role = role;
    }
}
