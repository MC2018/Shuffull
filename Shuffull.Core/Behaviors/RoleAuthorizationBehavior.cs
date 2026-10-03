using System.Reflection;
using MediatR;
using Nut.Results;
using Shuffull.Core.Authentication;

namespace Shuffull.Core.Behaviors;

/// <summary>
/// MediatR pipeline behavior that enforces <see cref="RequiresRoleAttribute"/> on a request before its handler
/// runs. A request without the attribute passes straight through; one that has it is allowed only when the
/// current user holds the required <see cref="Role"/>, otherwise the pipeline short-circuits to a
/// "Forbidden" <see cref="Result"/>/<see cref="Result{T}"/> and the handler never executes.
/// <para>
/// Registered after <see cref="ValidationBehavior{TRequest,TResponse}"/> so a request is validated first, then
/// authorized. Mirrors the Sociallite PolicyAuthorizationBehavior, simplified to role checks (no resource scope).
/// </para>
/// </summary>
public class RoleAuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var attribute = request.GetType().GetCustomAttribute<RequiresRoleAttribute>();
        if (attribute is null)
        {
            return await next();
        }

        if (RoleMembership.Has(currentUser.User, attribute.Role))
        {
            return await next();
        }

        return CreateForbiddenResult($"Forbidden: this action requires the {attribute.Role} role.");
    }

    /// <summary>
    /// Builds a failed <see cref="Result"/>/<see cref="Result{T}"/> of <typeparamref name="TResponse"/> via
    /// reflection, so this behavior works for any handler that returns a Result without per-handler boilerplate
    /// (same approach as <see cref="ValidationBehavior{TRequest,TResponse}"/>).
    /// </summary>
    private static TResponse CreateForbiddenResult(string message)
    {
        var responseType = typeof(TResponse);

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var innerType = responseType.GetGenericArguments()[0];
            var errorMethod = typeof(Result).GetMethod("Error", 1, [typeof(string)]);
            if (errorMethod != null)
            {
                var genericError = errorMethod.MakeGenericMethod(innerType);
                return (TResponse)genericError.Invoke(null, [message])!;
            }
        }

        if (responseType == typeof(Result))
        {
            return (TResponse)(object)Result.Error(message);
        }

        throw new InvalidOperationException(
            $"[RequiresRole] can only gate requests returning Result or Result<T>, but {responseType.Name} does not.");
    }
}
