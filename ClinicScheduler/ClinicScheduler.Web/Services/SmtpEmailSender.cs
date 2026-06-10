using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// MailKit-based SMTP sender. When unconfigured (no SMTP host), <see cref="IsConfigured"/>
/// is false and <see cref="SendAsync"/> logs and returns without sending, so the app
/// runs unchanged in environments without an email relay.
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IClinicEmailSender
{
    private EmailOptions Options => options.Value;

    /// <inheritdoc/>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Options.SmtpHost) &&
        !string.IsNullOrWhiteSpace(Options.FromAddress);

    /// <inheritdoc/>
    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogDebug("Email not configured; skipping send to {To}", toAddress);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(Options.FromName, Options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(Options.SmtpHost, Options.SmtpPort, SecureSocketOptions.StartTlsWhenAvailable, ct);
        if (!string.IsNullOrWhiteSpace(Options.SmtpUser))
            await client.AuthenticateAsync(Options.SmtpUser, Options.SmtpPassword, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
