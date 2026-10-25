using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.Api;

/// <summary>One reply from the Baytak API. Successes are JSON (objects, arrays or a JSON string message); errors are plain text.</summary>
public sealed record ApiReply(int Status, string Body)
{
    public bool Ok => Status is >= 200 and < 300;

    public JsonElement Json()
    {
        using var doc = JsonDocument.Parse(Body);
        return doc.RootElement.Clone();
    }

    /// <summary>The human readable text of the reply: the JSON string of a success message, or the plain-text error.</summary>
    public string Text
    {
        get
        {
            var body = Body.Trim();
            if (body.StartsWith('"'))
            {
                try
                {
                    return JsonSerializer.Deserialize<string>(body) ?? "";
                }
                catch (JsonException)
                {
                    return body;
                }
            }
            return body.Length == 0 ? $"The Baytak service answered {Status}." : body;
        }
    }

    /// <summary>True when the reply is a JSON string equal to one of the API's marker values ("NoAppartments", ...).</summary>
    public bool IsMarker(string marker) => Ok && Body.TrimStart().StartsWith('"') && Text == marker;
}

/// <summary>The API refused the signed-in session (expired, or the user signed out in the app). The client must sign in again.</summary>
public sealed class ApiSessionEndedException : Exception
{
    public ApiSessionEndedException() : base("The Baytak session has ended.") { }
}

/// <summary>
/// Calls the Baytak API as the signed-in user. Every rule (roles, ownership, receipts being immutable, totals computed on the
/// server) stays in the API; this app only translates between MCP and the API.
/// </summary>
public sealed class BaytakApi
{
    public const string HttpClientName = "baytak-api";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _http;
    private readonly McpOptions _options;
    private readonly Uri _base;

    public BaytakApi(IHttpClientFactory http, IOptions<McpOptions> options)
    {
        _http = http;
        _options = options.Value;
        if (!Uri.TryCreate(_options.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("Set Mcp:ApiBaseUrl to the Baytak API base URL, ending in /api.");
        _base = baseUri;
    }

    public Task<ApiReply> GetAsync(string path, string? jwt, CancellationToken ct) => SendAsync(HttpMethod.Get, path, jwt, null, ct);

    public Task<ApiReply> PostAsync(string path, string? jwt, object body, CancellationToken ct) => SendAsync(HttpMethod.Post, path, jwt, body, ct);

    /// <summary>Like <see cref="GetAsync"/>/<see cref="PostAsync"/> for signed-in calls, but a refused session throws.</summary>
    public async Task<ApiReply> CallAsync(HttpMethod method, string path, string jwt, object? body, CancellationToken ct)
    {
        var reply = await SendAsync(method, path, jwt, body, ct);
        if (reply.Status == 401) throw new ApiSessionEndedException();
        return reply;
    }

    private async Task<ApiReply> SendAsync(HttpMethod method, string path, string? jwt, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(_base, path));
        if (!string.IsNullOrEmpty(_options.ApiKey)) request.Headers.Add("x-functions-key", _options.ApiKey);
        if (jwt is not null) request.Headers.Authorization = new("Bearer", jwt);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, ct);
            return new ApiReply((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Unreachable or timed out: answer like a 503 so pages and tools can say so instead of failing.
            return new ApiReply(503, Unavailable);
        }
    }

    public const string Unavailable = "We couldn't reach Baytak right now. Please try again in a moment.";
}
