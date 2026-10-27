using Resend;
using Shuffull.Api.Configuration;
using Shuffull.Core.Alerts;
using Shuffull.Core.Alerts.Checks;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Api.Services.Alerts;

public static class AlertServiceCollectionExtensions
{
    /// <summary>Health-signal recording (used by handlers and services) plus the alert monitor and its checks.</summary>
    public static IServiceCollection AddAlerts(this IServiceCollection services, IConfiguration configuration)
    {
        var config = configuration.GetSection(AlertsConfiguration.AlertsConfigurationSection).Get<AlertsConfiguration>()
            ?? new AlertsConfiguration();
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHealthSignals, HealthSignals>();

        // When this API started: the monitor counts a funnel poll's silence from no earlier than this (see SignalAlertCheck).
        var startedUtc = DateTime.UtcNow;

        // Scoped, and resolved per monitor run, so the Resend client comes from IHttpClientFactory each run rather
        // than one HttpClient living as long as the process.
        if (config is { CanSendEmail: true, ResendApiKey: { } apiKey })
        {
            services.AddOptions();
            services.AddHttpClient<ResendClient>();
            services.Configure<ResendClientOptions>(o => o.ApiToken = apiKey);
            services.AddTransient<IResend, ResendClient>();
            services.AddScoped<IAlertMailer, ResendAlertMailer>();
        }
        else
        {
            services.AddScoped<IAlertMailer, LogOnlyAlertMailer>();
        }

        services.AddHttpClient<FunnelProbe>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IAlertProbe>(sp => sp.GetRequiredService<FunnelProbe>());

        services.AddScoped<IAlertCheck, SignalAlertCheck>(sp => new SignalAlertCheck(sp.GetRequiredService<ShuffullContext>(), startedUtc));
        services.AddScoped<IAlertCheck, LikeSyncBacklogCheck>();
        services.AddScoped<IAlertCheck, UntaggedKeptSongsCheck>();
        services.AddScoped<IAlertCheck, AbandonedMediaCheck>();

        services.AddHostedService<AlertMonitor>();
        return services;
    }
}
