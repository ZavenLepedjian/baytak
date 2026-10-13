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
                    _logger.LogWarning("Expo push service answered {Status}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Push notification delivery failed");
        }
    }
}
