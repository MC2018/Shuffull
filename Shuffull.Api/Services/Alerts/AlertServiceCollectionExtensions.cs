using Resend;
using Shuffull.Core.Persistence;
using Shuffull.Core.Services.Alerts;
using Shuffull.Core.Services.Alerts.Checks;

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

        if (config.CanSendEmail)
        {
            services.AddOptions();
            services.AddHttpClient<ResendClient>();
            services.Configure<ResendClientOptions>(o => o.ApiToken = config.ResendApiKey!);
            services.AddTransient<IResend, ResendClient>();
            services.AddSingleton<IAlertMailer, ResendAlertMailer>();
        }
        else
        {
            services.AddSingleton<IAlertMailer, LogOnlyAlertMailer>();
        }

        services.AddHttpClient<FunnelProbe>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IAlertProbe>(sp => sp.GetRequiredService<FunnelProbe>());

        services.AddScoped<IAlertCheck, SignalAlertCheck>(sp => new SignalAlertCheck(sp.GetRequiredService<ShuffullContext>()));
        services.AddScoped<IAlertCheck, LikeSyncBacklogCheck>();
        services.AddScoped<IAlertCheck, UntaggedKeptSongsCheck>();
        services.AddScoped<IAlertCheck, AbandonedMediaCheck>();

        services.AddHostedService<AlertMonitor>();
        return services;
    }
}
