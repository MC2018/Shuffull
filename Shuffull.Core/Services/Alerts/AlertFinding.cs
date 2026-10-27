using Nut.Results;

namespace Shuffull.Core.Services.Alerts;

/// <summary>
/// One problem a check sees right now. The monitor turns findings into emails: the first time a key appears, when
/// its <see cref="Level"/> rises, and every <see cref="RemindEvery"/> while it lasts (null: never). A key the check
/// stops reporting is resolved.
/// </summary>
/// <param name="Key">Stable id; must start with the check's <see cref="IAlertCheck.KeyPrefix"/> and a colon.</param>
/// <param name="Level">Higher is worse. Only a rise sends a new email.</param>
/// <param name="Subject">Short, says what broke.</param>
/// <param name="Body">Plain text: what is wrong, since when, and what to do.</param>
public sealed record AlertFinding(string Key, int Level, string Subject, string Body, TimeSpan? RemindEvery = null);

/// <summary>
/// Looks at durable state (the DB, health signals) and reports what is wrong. Runs every few
/// minutes, so it must be cheap and read-only. Return an error Result when the check couldn't look at all: its
/// open alerts are then left as they are rather than resolved by mistake.
/// </summary>
public interface IAlertCheck
{
    /// <summary>The check owns every alert key starting with this and a colon.</summary>
    string KeyPrefix { get; }

    Task<Result<IReadOnlyList<AlertFinding>>> EvaluateAsync(DateTime nowUtc, CancellationToken cancellationToken);
}

/// <summary>
/// Goes and tries something (an HTTP call, a signed-in fetch) and records the outcome as a health signal. The
/// monitor runs every probe before the checks, so the signal rules see this run's result. A probe paces itself;
/// it is called every monitor run.
/// </summary>
public interface IAlertProbe
{
    Task RunAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
