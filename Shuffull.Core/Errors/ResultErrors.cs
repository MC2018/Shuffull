using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Shuffull.Core.Errors;

/// <summary>Builds and reads <see cref="KindedError"/>s.</summary>
public static class ResultErrors
{
    /// <summary>The kind of any error; one that isn't a <see cref="KindedError"/> is <see cref="ErrorKind.Invalid"/>.</summary>
    public static ErrorKind KindOf(Exception error) => error is KindedError kinded ? kinded.Kind : ErrorKind.Invalid;

    public static KindedError NotFound(string message) => new(ErrorKind.NotFound, message);

    public static KindedError Forbidden(string message) => new(ErrorKind.Forbidden, message);

    public static KindedError Conflict(string message) => new(ErrorKind.Conflict, message);

    /// <summary>The same kind of error under a new message, for a handler that adds its own context.</summary>
    public static KindedError WithMessage(this Exception error, string message) => new(KindOf(error), message, error);

    /// <summary>
    /// For a lookup that failed: <paramref name="message"/> as a 404 when it simply found nothing, otherwise the
    /// lookup's own error. A database outage must not be answered as "not found".
    /// </summary>
    public static Exception NotFoundAs(this Exception error, string message)
        => KindOf(error) == ErrorKind.NotFound ? NotFound(message) : error;

    /// <summary>
    /// Classifies a caught exception. Database faults are sorted by SQLSTATE class: a constraint violation (23) is a
    /// <see cref="ErrorKind.Conflict"/>, bad data (22) is <see cref="ErrorKind.Invalid"/>, and the rest (connection,
    /// timeout, deadlock, serialization, a missing table) are a <see cref="ErrorKind.ServerFault"/>. Classes 22 and
    /// 23 stay 4xx because they depend on the request: replaying it fails the same way, and a 5xx would stop the app's
    /// sync on it forever. Anything else that isn't a database fault gets <paramref name="otherwise"/>.
    /// </summary>
    public static KindedError FromException(Exception exception, string message, ErrorKind otherwise = ErrorKind.Invalid)
        => new(Classify(exception, otherwise), message, exception);

    private static ErrorKind Classify(Exception exception, ErrorKind otherwise)
    {
        if (exception is KindedError kinded)
        {
            return kinded.Kind;
        }
        if (exception is DbUpdateConcurrencyException)
        {
            return ErrorKind.Conflict;
        }

        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is DbException db)
            {
                // Npgsql sets SQLSTATE on a PostgresException; a connection failure or timeout has none.
                return db.SqlState switch
                {
                    { } state when state.StartsWith("23") => ErrorKind.Conflict,
                    { } state when state.StartsWith("22") => ErrorKind.Invalid,
                    _ => ErrorKind.ServerFault,
                };
            }
            if (e is TimeoutException)
            {
                return ErrorKind.ServerFault;
            }
        }

        // A DbUpdateException always wraps the provider's error, so this is only reached without one.
        return exception is DbUpdateException ? ErrorKind.ServerFault : otherwise;
    }
}
