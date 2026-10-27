using Nut.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shuffull.Api.Services.Alerts;
using Shuffull.Core.Tests.Infrastructure;

namespace Shuffull.Core.Tests.Services.Alerts;

/// <summary><see cref="AlertServiceCollectionExtensions.AddAlerts"/> picks the mailer from config.</summary>
public class AlertWiringTest
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAlerts(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void NoKeyOrRecipient_LogsInsteadOfSending()
    {
        using var noKey = Build(new() { ["Alerts:To"] = "someone@example.com" });
        Assert.IsType<LogOnlyAlertMailer>(noKey.GetRequiredService<IAlertMailer>());

        using var noRecipient = Build(new() { ["Alerts:ResendApiKey"] = "re_test" });
        Assert.IsType<LogOnlyAlertMailer>(noRecipient.GetRequiredService<IAlertMailer>());
    }

    [Fact]
    public void KeyAndRecipient_SendThroughResend()
    {
        using var provider = Build(new() { ["Alerts:ResendApiKey"] = "re_test", ["Alerts:To"] = "someone@example.com" });
        Assert.IsType<ResendAlertMailer>(provider.GetRequiredService<IAlertMailer>());
    }

    [LiveEmailFact]
    public async Task LiveSend_ReachesResend()
    {
        using var provider = Build(new()
        {
            ["Alerts:ResendApiKey"] = LiveEmailFactAttribute.Key,
            ["Alerts:To"] = LiveEmailFactAttribute.To,
        });

        var result = await provider.GetRequiredService<IAlertMailer>().SendAsync(
            "[Shuffull] Test: alert email wiring",
            "A test from Shuffull.Core.Tests (AlertWiringTest.LiveSend_ReachesResend). No action needed.",
            CancellationToken.None);

        Assert.True(result.IsOk, result.IsError ? result.GetError().Message : null);
    }
}
