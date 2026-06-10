using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Sends SMS through the Twilio Messages API using a plain HttpClient (no SDK
/// dependency). When unconfigured, <see cref="IsConfigured"/> is false and
/// <see cref="SendAsync"/> logs and returns without sending.
/// </summary>
public sealed class TwilioSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsOptions> options,
    ILogger<TwilioSmsSender> logger) : ISmsSender
{
    private SmsOptions Options => options.Value;

    /// <inheritdoc/>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Options.AccountSid) &&
        !string.IsNullOrWhiteSpace(Options.AuthToken) &&
        !string.IsNullOrWhiteSpace(Options.FromNumber);

    /// <inheritdoc/>
    public async Task SendAsync(string toPhone, string message, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogDebug("SMS not configured; skipping send to {To}", toPhone);
            return;
        }

        var client = httpClientFactory.CreateClient("twilio");
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{Options.AccountSid}:{Options.AuthToken}"));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Options.AccountSid}/Messages.json")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Basic", credentials) },
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = toPhone,
                ["From"] = Options.FromNumber,
                ["Body"] = message
            })
        };

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Twilio returned {(int)response.StatusCode} sending SMS: {body}");
        }
    }
}
