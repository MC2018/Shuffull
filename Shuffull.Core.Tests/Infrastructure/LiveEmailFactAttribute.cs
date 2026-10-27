namespace Shuffull.Core.Tests.Infrastructure;

/// <summary>
/// A [Fact] that runs only when <see cref="KeyVariable"/> holds a Resend API key and <see cref="ToVariable"/> a
/// recipient; otherwise it is reported as skipped. It sends a real email, so it is for proving the wiring by hand.
/// </summary>
public sealed class LiveEmailFactAttribute : FactAttribute
{
    public const string KeyVariable = "SHUFFULL_TEST_RESEND_KEY";
    public const string ToVariable = "SHUFFULL_TEST_ALERT_TO";

    public static string? Key => Environment.GetEnvironmentVariable(KeyVariable);
    public static string? To => Environment.GetEnvironmentVariable(ToVariable);

    public LiveEmailFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(To))
        {
            Skip = $"Set {KeyVariable} and {ToVariable} to send a real test email.";
        }
    }
}
