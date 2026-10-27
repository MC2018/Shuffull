using Shuffull.Core.Models.Database;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Alerts;

public enum AlertDecisionKind
{
    /// <summary>Nothing to send or change.</summary>
    None,
    /// <summary>A new problem: email it.</summary>
    Fire,
    /// <summary>It got worse: email it.</summary>
    Escalate,
    /// <summary>Still going and a reminder is due: email it.</summary>
    Remind,
    /// <summary>Still going, nothing to email; record the current level and subject.</summary>
    Update,
    /// <summary>The check stopped reporting it: start the clear timer, no email yet.</summary>
    StartClearing,
    /// <summary>Clear for long enough: email that it's resolved.</summary>
    Resolve,
}

/// <summary>
/// Decides what one alert key needs this run, from what was last sent (<see cref="AlertState"/>) and what the check
/// sees now (<see cref="AlertFinding"/>). Pure, so every transition is unit-tested; the monitor does the sending.
/// </summary>
public static class AlertEvaluator
{
    public static AlertDecisionKind Decide(AlertState? state, AlertFinding? finding, DateTime nowUtc, TimeSpan resolveAfter)
    {
        if (state is not { IsFiring: true } open)
        {
            return finding != null ? AlertDecisionKind.Fire : AlertDecisionKind.None;
        }

        if (finding != null)
        {
            if (finding.Level > open.Level)
            {
                return AlertDecisionKind.Escalate;
            }

            if (finding.RemindEvery is { } every && open.LastNotifiedUtc is { } sent && nowUtc - sent >= every)
            {
                return AlertDecisionKind.Remind;
            }

            return finding.Level != open.Level || finding.Subject != open.Subject || open.ClearSinceUtc != null
                ? AlertDecisionKind.Update
                : AlertDecisionKind.None;
        }

        if (open.ClearSinceUtc is not { } clearSince)
        {
            return AlertDecisionKind.StartClearing;
        }

        return nowUtc - clearSince >= resolveAfter ? AlertDecisionKind.Resolve : AlertDecisionKind.None;
    }
}
