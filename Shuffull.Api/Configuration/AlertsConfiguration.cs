namespace Shuffull.Api.Configuration;

/// <summary>
/// The <c>Alerts</c> section. Email goes out only with a Resend key and a recipient; without them every alert is
/// logged instead, so dev and test runs need no key. The key and the recipient come from env only
/// (<c>Alerts__ResendApiKey</c>, <c>Alerts__To</c>), never from a committed appsettings file: this repo is public.
/// </summary>
public class AlertsConfiguration
{
    public const string AlertsConfigurationSection = "Alerts";

    public string? ResendApiKey { get; set; }

    public string From { get; set; } = "Shuffull Alerts <alerts@clausius.app>";

    public string? To { get; set; }

    /// <summary>Prepended to every subject, so these sort together in the inbox.</summary>
    public string SubjectPrefix { get; set; } = "[Shuffull]";

    /// <summary>How often the monitor runs its checks.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Wait after startup before the first run, so the funnel and the DB are up first.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a check must stay clear before the alert is resolved, so a flapping check stays quiet.</summary>
    public TimeSpan ResolveAfter { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The funnel web API's health route, over the shared <c>shuffull-ingest-net</c>. Blank turns the probe off.</summary>
    public string FunnelHealthUrl { get; set; } = "http://youtubefunnel.web-api:8080/api/health";

    public bool CanSendEmail => !string.IsNullOrWhiteSpace(ResendApiKey) && !string.IsNullOrWhiteSpace(To);
}
