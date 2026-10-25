using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Api.Services;

public sealed class PushOptions
{
    /// <summary>Optional Expo access token (needed only if "enhanced push security" is enabled for the project).</summary>
    public string AccessToken { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public interface IPushSender
{
    /// <summary>Best effort: never throws. Invalid tokens are skipped.</summary>
    Task SendAsync(IEnumerable<string?> tokens, string title, string body, CancellationToken ct);
}

public static partial class ExpoPushToken
{
    [GeneratedRegex(@"^Expo(nent)?PushToken\[[^\]\s]{10,200}\]$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? token) => !string.IsNullOrWhiteSpace(token) && Pattern().IsMatch(token);
}

/// <summary>Sends notifications through the Expo push service.</summary>
public sealed class ExpoPushSender : IPushSender
{
    public const string HttpClientName = "expo-push";
    private const int BatchSize = 100;

    private readonly IHttpClientFactory _http;
    private readonly PushOptions _options;
    private readonly ILogger<ExpoPushSender> _logger;

    public ExpoPushSender(IHttpClientFactory http, IOptions<PushOptions> options, ILogger<ExpoPushSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(IEnumerable<string?> tokens, string title, string body, CancellationToken ct)
    {
        if (!_options.Enabled) return;
        var valid = tokens.Where(ExpoPushToken.IsValid).Distinct().Cast<string>().ToList();
        if (valid.Count == 0) return;

        try
        {
            var client = _http.CreateClient(HttpClientName);
            foreach (var batch in valid.Chunk(BatchSize))
            {
                var payload = batch.Select(t => new { to = t, title, body, sound = "default" });
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://exp.host/--/api/v2/push/send")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(_options.AccessToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Expo push service answered {Status}", (int)response.StatusCode);
                    continue;
                }
                LogRejections(await response.Content.ReadAsStringAsync(ct), batch.Length);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Push notification delivery failed");
        }
    }

    /// <summary>
    /// Expo answers 200 even when it refuses a push (a device that is gone, missing Firebase or Apple credentials, a bad token),
    /// so the per-push tickets are read and every refusal is logged. Tokens themselves are never logged.
    /// </summary>
    internal void LogRejections(string body, int sent)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var tickets) || tickets.ValueKind != JsonValueKind.Array) return;
            var refused = 0;
            foreach (var ticket in tickets.EnumerateArray())
            {
                if (ticket.TryGetProperty("status", out var status) && status.GetString() == "ok") continue;
                refused++;
                var code = ticket.TryGetProperty("details", out var details) && details.TryGetProperty("error", out var e) ? e.GetString() : null;
                var message = ticket.TryGetProperty("message", out var m) ? m.GetString() : null;
                _logger.LogWarning("Expo refused a push: {Error}. {Message}", code ?? "unknown", message);
            }
            _logger.LogInformation("Expo push: {Accepted} of {Sent} accepted", sent - refused, sent);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Expo push service sent an answer that could not be read");
        }
    }
}
