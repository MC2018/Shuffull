namespace Shuffull.Core.Errors;

/// <summary>
/// What kind of failure a <c>Result</c> error is. It decides the HTTP status the API answers with
/// (<c>ControllerBaseExtensions</c> in Shuffull.Api), and that status decides what the app's outbox does with the
/// queued request: a 4xx is kept for a while and then dead-lettered, a 5xx stops the sync and is retried next time.
/// So <see cref="ServerFault"/> is only for faults that clear up by themselves. A request that would fail the same
/// way every time it is replayed must get a 4xx, or it would stop the app's sync for good.
/// </summary>
public enum ErrorKind
{
    /// <summary>400. The request is wrong: validation, or a value the database refused. Any error that isn't a
    /// <see cref="KindedError"/> counts as this, which is how every error was answered before kinds existed.</summary>
    Invalid,

    /// <summary>403. The caller lacks the role the request needs.</summary>
    Forbidden,

    /// <summary>404. What the request names doesn't exist (or isn't the caller's).</summary>
    NotFound,

    /// <summary>409. The request clashes with the data as it is now: a constraint it would break, or a row that
    /// changed under it.</summary>
    Conflict,

    /// <summary>500. The server failed, not the request: the database unreachable, a timeout, a deadlock.</summary>
    ServerFault,
}
