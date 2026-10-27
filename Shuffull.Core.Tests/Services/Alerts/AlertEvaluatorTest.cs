using Shuffull.Core.Models.Database;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Core.Tests.Services.Alerts;

/// <summary>Every transition of one alert key: what was last emailed vs. what the check sees now.</summary>
public class AlertEvaluatorTest
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ResolveAfter = TimeSpan.FromMinutes(15);

    private static AlertFinding Finding(int level = 1, string subject = "Broken", TimeSpan? remind = null)
        => new("x:y", level, subject, "body", remind);

    private static AlertState Firing(int level = 1, string subject = "Broken", DateTime? notified = null)
    {
        var state = AlertState.Create("x:y");
        state.Notified(level, subject, notified ?? Now.AddHours(-1));
        return state;
    }

    private static AlertDecisionKind Decide(AlertState? state, AlertFinding? finding, DateTime? now = null)
        => AlertEvaluator.Decide(state, finding, now ?? Now, ResolveAfter);

    [Fact]
    public void NewFinding_Fires() => Assert.Equal(AlertDecisionKind.Fire, Decide(null, Finding()));

    [Fact]
    public void FindingOnAResolvedKey_FiresAgain()
    {
        var state = Firing();
        state.Resolved(Now.AddMinutes(-5));
        Assert.Equal(AlertDecisionKind.Fire, Decide(state, Finding()));
    }

    [Fact]
    public void SameLevelAndSubject_SendsNothing() => Assert.Equal(AlertDecisionKind.None, Decide(Firing(), Finding()));

    [Fact]
    public void HigherLevel_Escalates() => Assert.Equal(AlertDecisionKind.Escalate, Decide(Firing(level: 1), Finding(level: 2)));

    [Fact]
    public void LowerLevel_UpdatesQuietly() => Assert.Equal(AlertDecisionKind.Update, Decide(Firing(level: 2), Finding(level: 1)));

    [Fact]
    public void ChangedSubject_UpdatesQuietly()
        => Assert.Equal(AlertDecisionKind.Update, Decide(Firing(subject: "3 broken"), Finding(subject: "4 broken")));

    [Fact]
    public void ReminderDue_Reminds()
        => Assert.Equal(AlertDecisionKind.Remind,
            Decide(Firing(notified: Now.AddDays(-1)), Finding(remind: TimeSpan.FromDays(1))));

    [Fact]
    public void ReminderNotYetDue_SendsNothing()
        => Assert.Equal(AlertDecisionKind.None,
            Decide(Firing(notified: Now.AddHours(-23)), Finding(remind: TimeSpan.FromDays(1))));

    [Fact]
    public void NoReminderSet_NeverReminds()
        => Assert.Equal(AlertDecisionKind.None, Decide(Firing(notified: Now.AddDays(-30)), Finding()));

    [Fact]
    public void FindingGone_StartsClearing_ThenResolvesAfterTheWindow()
    {
        var state = Firing();
        Assert.Equal(AlertDecisionKind.StartClearing, Decide(state, null));

        state.StartedClearing(Now);
        Assert.Equal(AlertDecisionKind.None, Decide(state, null, Now.AddMinutes(14)));
        Assert.Equal(AlertDecisionKind.Resolve, Decide(state, null, Now.AddMinutes(15)));
    }

    [Fact]
    public void FindingBackWhileClearing_CancelsTheResolveWithoutANewEmail()
    {
        var state = Firing();
        state.StartedClearing(Now.AddMinutes(-10));

        Assert.Equal(AlertDecisionKind.Update, Decide(state, Finding()));
        state.StillFiring(1, "Broken");
        Assert.Null(state.ClearSinceUtc);
        // The clear timer starts over, so it can't resolve off the earlier, interrupted clear.
        Assert.Equal(AlertDecisionKind.StartClearing, Decide(state, null, Now.AddHours(1)));
    }

    [Fact]
    public void NothingFiringAndNoFinding_DoesNothing()
    {
        Assert.Equal(AlertDecisionKind.None, Decide(null, null));
        var resolved = Firing();
        resolved.Resolved(Now);
        Assert.Equal(AlertDecisionKind.None, Decide(resolved, null));
    }
}
