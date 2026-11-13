using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Shuffull.Core.Models.Database;

namespace Shuffull.Api.Tools.Authorization;

/// <summary>
/// Binds an action's <see cref="User"/> parameter to the signed-in caller (what <see cref="JwtMiddleware"/> attached),
/// so a controller never reads <c>HttpContext.Items</c> itself:
/// <code>GetCurrent([FromCurrentUser] User user, CancellationToken cancellationToken)</code>
/// Any action with such a parameter is behind <see cref="AuthorizeAttribute"/> even if it doesn't say so
/// (<see cref="CurrentUserRequiresSignInConvention"/>), so an unauthenticated call is a 401 (503 when the user lookup
/// failed) before binding runs, and can't reach the action with no user.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromCurrentUserAttribute : Attribute, IBinderTypeProviderMetadata
{
    /// <summary>Not read from the request, so model binding and the API explorer leave it alone.</summary>
    public BindingSource BindingSource => BindingSource.Special;

    public Type BinderType => typeof(CurrentUserModelBinder);
}

/// <summary>The binder behind <see cref="FromCurrentUserAttribute"/>.</summary>
public sealed class CurrentUserModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext.HttpContext.Items[JwtMiddleware.UserKey] is not User user)
        {
            // The convention puts [Authorize] in front of every such action, so this means the convention was bypassed.
            throw new InvalidOperationException(
                $"[FromCurrentUser] on {bindingContext.ActionContext.ActionDescriptor.DisplayName} ran without a signed-in user; the action must be behind [Authorize].");
        }

        bindingContext.Result = ModelBindingResult.Success(user);
        // It's the database row, not input: its annotations mustn't turn into a 400 from [ApiController].
        bindingContext.ValidationState[user] = new ValidationStateEntry { SuppressValidation = true };
        return Task.CompletedTask;
    }
}
