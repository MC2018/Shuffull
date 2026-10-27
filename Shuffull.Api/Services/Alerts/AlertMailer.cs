using Microsoft.Extensions.Logging;
using Nut.Results;
using Resend;
using Shuffull.Api.Configuration;

namespace Shuffull.Api.Services.Alerts;

public interface IAlertMailer
{
    Task<Result> SendAsync(string subject, string textBody, CancellationToken cancellationToken);
}

/// <summary>Sends through Resend (the same client SocialLite uses). A failed send is an error Result, so the monitor
/// leaves the alert unsent and tries again next run.</summary>
public sealed class ResendAlertMailer(IResend resend, AlertsConfiguration config, ILogger<ResendAlertMailer> logger) : IAlertMailer
{
    private readonly string _to = config.To
        ?? throw new InvalidOperationException("ResendAlertMailer needs Alerts:To; AddAlerts registers it only then.");

    public async Task<Result> SendAsync(string subject, string textBody, CancellationToken cancellationToken)
    {
        try
        {
            var message = new EmailMessage
            {
                From = config.From,
                Subject = subject,
                TextBody = textBody,
            };
            message.To.Add(_to);

            await resend.EmailSendAsync(message, cancellationToken);
            logger.LogInformation("Sent alert email: {Subject}", subject);
            return Result.Ok();
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "Could not send the alert email {Subject}; trying again next run.", subject);
            return Result.Error($"Failed to send alert email: {e.Message}");
        }
    }
}

/// <summary>Used when no Resend key or recipient is configured: the alert goes to the log and counts as sent.</summary>
public sealed class LogOnlyAlertMailer(ILogger<LogOnlyAlertMailer> logger) : IAlertMailer
{
    public Task<Result> SendAsync(string subject, string textBody, CancellationToken cancellationToken)
    {
        logger.LogWarning("Alert (email is not configured): {Subject}\n{Body}", subject, textBody);
        return Task.FromResult(Result.Ok());
    }
}
