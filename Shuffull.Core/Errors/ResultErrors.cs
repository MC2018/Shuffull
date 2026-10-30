using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Shuffull.Core.Errors;

/// <summary>Builds and reads <see cref="KindedError"/>s.</summary>
public static class ResultErrors
{
    private static readonly HashSet<string> TransientSqlStateClasses = ["08", "40", "53", "57", "58"];

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
    /// Classifies a caught exception. Database faults are sorted by SQLSTATE: a constraint violation (class 23) is a
    /// <see cref="ErrorKind.Conflict"/>, and only the classes that clear up by themselves are a
    /// <see cref="ErrorKind.ServerFault"/>: a lost connection (08), a deadlock or serialization failure (40), the server
    /// out of resources (53), shut down or cancelled (57), a system error (58), a lock wait that timed out (55P03), or no
    /// SQLSTATE at all (the connection failed before the server answered, or timed out). Every other class depends on
    /// the request: bad data (22), a value too big to index (54), and so on. Replaying it fails the same way, and a 5xx
    /// would stop the app's sync on it for good, so it is <see cref="ErrorKind.Invalid"/>. Anything that isn't a
    /// database fault gets <paramref name="otherwise"/>.
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
                    null => ErrorKind.ServerFault,
                    "55P03" => ErrorKind.ServerFault,
                    _ when db.SqlState.StartsWith("23") => ErrorKind.Conflict,
                    _ when TransientSqlStateClasses.Contains(db.SqlState[..Math.Min(2, db.SqlState.Length)]) => ErrorKind.ServerFault,
                    _ => ErrorKind.Invalid,
                };
            }
            if (e is TimeoutException)
            {
                return ErrorKind.ServerFault;
            }
        }

        // A save that failed without a provider error inside (EF or the driver refusing a value it was given, such as a
        // non-UTC timestamp) fails the same way on every retry, so it is not a ServerFault either.
        return otherwise;
    }
}
