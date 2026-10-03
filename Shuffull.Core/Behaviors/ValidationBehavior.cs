using FluentValidation;
using MediatR;
using Nut.Results;

namespace Shuffull.Core.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs FluentValidation validators before the handler.
/// Automatically returns Result.Error if validation fails.
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (!failures.Any())
            return await next();

        var errorMessage = string.Join("; ", failures.Select(f => f.ErrorMessage));

        // Create Result.Error<T>(message) via reflection so this behavior works
        // for any Result<T> response type without per-handler boilerplate.
        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var innerType = responseType.GetGenericArguments()[0];
            var errorMethod = typeof(Result).GetMethod("Error", 1, [typeof(string)]);
            if (errorMethod != null)
            {
                var genericError = errorMethod.MakeGenericMethod(innerType);
                return (TResponse)genericError.Invoke(null, [errorMessage])!;
            }
        }

        return await next();
    }
}
