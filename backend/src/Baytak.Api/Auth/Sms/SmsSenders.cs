using System.Net.Http.Headers;
using System.Text;
using Baytak.Api.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Api.Auth.Sms;

public interface ISmsSender
{
    /// <summary>Sends a text message. Throws when delivery could not be started.</summary>
    Task SendAsync(string toE164, string body, CancellationToken ct);
}

public sealed class SmsOptions
{
    /// <summary>
    /// "Twilio" (the API makes the code and sends it through Twilio Messaging), "TwilioVerify" (Twilio Verify makes, sends
    /// and checks the code), or "Log" (development only: codes are written to the log).
    /// </summary>
    public string Provider { get; set; } = "Twilio";
    /// <summary>
    /// Comma separated country calling codes that may receive SMS (default "961", Lebanon).
    /// Blocks SMS-pumping / toll fraud through the anonymous login endpoint. "*" allows every country.
    /// </summary>
    public string AllowedCountryCodes { get; set; } = "961";
    public TwilioOptions Twilio { get; set; } = new();

    public bool IsAllowedDestination(string e164)
    {
        var allowed = AllowedCountryCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowed.Contains("*")) return true;
        return e164.StartsWith('+') && allowed.Any(cc => e164.AsSpan(1).StartsWith(cc.TrimStart('+')));
    }
}

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    /// <summary>Sender number in E.164 format. Ignored when MessagingServiceSid is set.</summary>
    public string From { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;
    /// <summary>Twilio Verify service (VA...), used when Sms:Provider is "TwilioVerify".</summary>
    public string VerifyServiceSid { get; set; } = string.Empty;
}

/// <summary>Sends SMS through the Twilio REST API (no SDK dependency).</summary>
public sealed class TwilioSmsSender : ISmsSender
{
    public const string HttpClientName = "twilio";
    private readonly IHttpClientFactory _http;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(IHttpClientFactory http, IOptions<SmsOptions> options, ILogger<TwilioSmsSender> logger)
    {
        _http = http;
        _options = options.Value.Twilio;
        _logger = logger;
    }

    public async Task SendAsync(string toE164, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken) ||
            (string.IsNullOrWhiteSpace(_options.From) && string.IsNullOrWhiteSpace(_options.MessagingServiceSid)))
            throw new InvalidOperationException("Twilio is not configured (Sms:Twilio:AccountSid, AuthToken and From or MessagingServiceSid).");

        var form = new Dictionary<string, string> { ["To"] = toE164, ["Body"] = body };
        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid)) form["MessagingServiceSid"] = _options.MessagingServiceSid;
        else form["From"] = _options.From;

        var client = _http.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form),
        };
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Only Twilio's numeric error code is logged: the raw body echoes the destination number (PII).
            var errorCode = await ReadTwilioErrorCodeAsync(response, ct);
            _logger.LogError("Twilio rejected the SMS to {Phone}: HTTP {Status}, Twilio code {Code}", PhoneNumber.Mask(toE164), (int)response.StatusCode, errorCode);
            throw new HttpRequestException($"Twilio returned {(int)response.StatusCode}");
        }
    }

    private static async Task<string> ReadTwilioErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.ToString() : "unknown";
        }
        catch (System.Text.Json.JsonException)
        {
            return "unknown";
        }
    }
}

/// <summary>
/// A service that sends and checks verification codes itself (Twilio Verify). When one is registered, the API stores no
/// code of its own; its rate limits, attempt limit and test numbers still apply in front of it.
/// </summary>
public interface IExternalVerifier
{
    /// <summary>Starts a verification: the provider sends a code to the number. Throws when it could not be started.</summary>
    Task StartAsync(string toE164, CancellationToken ct);

    /// <summary>True when the code is correct for the number's pending verification.</summary>
    Task<bool> CheckAsync(string toE164, string code, CancellationToken ct);
}

/// <summary>Twilio Verify v2 through its REST API (no SDK dependency).</summary>
public sealed class TwilioVerifier : IExternalVerifier
{
    private readonly IHttpClientFactory _http;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioVerifier> _logger;

    public TwilioVerifier(IHttpClientFactory http, IOptions<SmsOptions> options, ILogger<TwilioVerifier> logger)
    {
        _http = http;
        _options = options.Value.Twilio;
        _logger = logger;
    }

    public async Task StartAsync(string toE164, CancellationToken ct)
    {
        using var response = await PostAsync("Verifications", new() { ["To"] = toE164, ["Channel"] = "sms" }, ct);
        if (!response.IsSuccessStatusCode)
        {
            var code = await ReadTwilioErrorCodeAsync(response, ct);
            _logger.LogError("Twilio Verify did not start for {Phone}: HTTP {Status}, Twilio code {Code}", PhoneNumber.Mask(toE164), (int)response.StatusCode, code);
            throw new HttpRequestException($"Twilio Verify returned {(int)response.StatusCode}");
        }
    }

    public async Task<bool> CheckAsync(string toE164, string code, CancellationToken ct)
    {
        using var response = await PostAsync("VerificationCheck", new() { ["To"] = toE164, ["Code"] = code }, ct);
        // 404: no pending verification any more (expired, already approved, or too many attempts).
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadTwilioErrorCodeAsync(response, ct);
            _logger.LogError("Twilio Verify check failed for {Phone}: HTTP {Status}, Twilio code {Code}", PhoneNumber.Mask(toE164), (int)response.StatusCode, error);
            throw new HttpRequestException($"Twilio Verify returned {(int)response.StatusCode}");
        }
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "approved";
    }

    private async Task<HttpResponseMessage> PostAsync(string resource, Dictionary<string, string> form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken) || string.IsNullOrWhiteSpace(_options.VerifyServiceSid))
            throw new InvalidOperationException("Twilio Verify is not configured (Sms:Twilio:AccountSid, AuthToken and VerifyServiceSid).");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://verify.twilio.com/v2/Services/{_options.VerifyServiceSid}/{resource}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return await _http.CreateClient(TwilioSmsSender.HttpClientName).SendAsync(request, ct);
    }

    private static async Task<string> ReadTwilioErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.ToString() : "unknown";
        }
        catch (System.Text.Json.JsonException)
        {
            return "unknown";
        }
    }
}

/// <summary>Development sender: prints the message instead of sending it. Never use in production.</summary>
public sealed class LogSmsSender : ISmsSender
{
    private readonly ILogger<LogSmsSender> _logger;
    public LogSmsSender(ILogger<LogSmsSender> logger) => _logger = logger;

    public Task SendAsync(string toE164, string body, CancellationToken ct)
    {
        _logger.LogWarning("[DEV SMS] to {Phone}: {Body}", toE164, body);
        return Task.CompletedTask;
    }
}
