using Shuffull.Api.Configuration;
using Shuffull.Core.Alerts;
using Shuffull.Core.Services.Alerts;

namespace Shuffull.Api.Services.Alerts;

/// <summary>
/// Calls the funnel's health route every monitor run and records <see cref="ShuffullSignalRules.FunnelProbe"/>, which
/// alerts after three misses in a row. A stopped funnel can't report itself, so the site does; the funnel probes the
/// site's public URL the same way (YoutubeFunnel#31).
/// </summary>
public sealed class FunnelProbe(HttpClient httpClient, AlertsConfiguration config, IHealthSignals signals,
    ILogger<FunnelProbe> logger) : IAlertProbe
{
    public async Task RunAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.FunnelHealthUrl))
        {
            return;
        }

        try
        {
            using var response = await httpClient.GetAsync(config.FunnelHealthUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await signals.SuccessAsync(ShuffullSignalRules.FunnelProbe, cancellationToken);
                return;
            }

            await signals.FailureAsync(ShuffullSignalRules.FunnelProbe,
                $"GET {config.FunnelHealthUrl} returned {(int)response.StatusCode} {response.StatusCode}.", cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Funnel probe failed: {Error}", e.Message);
            await signals.FailureAsync(ShuffullSignalRules.FunnelProbe, $"GET {config.FunnelHealthUrl} failed: {e.Message}",
                cancellationToken);
        }
    }
}
