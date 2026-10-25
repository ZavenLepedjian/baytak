using System.Collections.Concurrent;
using System.Text.Json;
using Baytak.Mcp.Security;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.OAuth;

public sealed record OAuthClient(string ClientId, string Name, IReadOnlyList<string> RedirectUris);

/// <summary>
/// Identifies OAuth clients without a database. Two ways are supported, as ChatGPT and Claude use them:
/// dynamic client registration (the client_id is the sealed registration) and Client ID Metadata Documents
/// (the client_id is an https URL on an allowed host that describes the client).
/// </summary>
public sealed class ClientRegistry
{
    public const string HttpClientName = "client-metadata";
    private const string RegisteredPrefix = "bk_";
    private const int MaxMetadataBytes = 64 * 1024;
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly Sealer _sealer;
    private readonly IHttpClientFactory _http;
    private readonly McpOptions _options;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (OAuthClient Client, DateTimeOffset Until)> _metadataCache = new();

    public ClientRegistry(Sealer sealer, IHttpClientFactory http, IOptions<McpOptions> options, TimeProvider time)
    {
        _sealer = sealer;
        _http = http;
        _options = options.Value;
        _time = time;
    }

    public string Register(string name, string[] redirectUris) =>
        RegisteredPrefix + _sealer.Seal(Purpose.Client, new ClientRecord(name, redirectUris));

    /// <summary>Null when the URI may be used as a redirect target, otherwise the reason it may not.</summary>
    public string? CheckRedirectUri(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return "redirect_uri must be an absolute URL";
        if (!string.IsNullOrEmpty(parsed.Fragment)) return "redirect_uri must not have a fragment";
        var loopback = parsed.IsLoopback;
        if (parsed.Scheme != Uri.UriSchemeHttps && !(loopback && parsed.Scheme == Uri.UriSchemeHttp)) return "redirect_uri must use https";
        if (!McpOptions.HostAllowed(parsed.Host, _options.RedirectHosts)) return $"redirect_uri host {parsed.Host} is not allowed";
        return null;
    }

    public async Task<(OAuthClient? Client, string? Error)> ResolveAsync(string? clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return (null, "client_id is required");

        if (clientId.StartsWith(RegisteredPrefix, StringComparison.Ordinal))
        {
            var record = _sealer.Open<ClientRecord>(Purpose.Client, clientId[RegisteredPrefix.Length..]);
            return record is null ? (null, "Unknown client") : (new OAuthClient(clientId, record.Name, record.RedirectUris), null);
        }

        if (Uri.TryCreate(clientId, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
            return await FromMetadataDocumentAsync(clientId, url, ct);

        return (null, "Unknown client");
    }

    private async Task<(OAuthClient? Client, string? Error)> FromMetadataDocumentAsync(string clientId, Uri url, CancellationToken ct)
    {
        if (!McpOptions.HostAllowed(url.Host, _options.ClientMetadataHosts) || !url.IsDefaultPort)
            return (null, $"Client metadata from {url.Host} is not accepted");

        var now = _time.GetUtcNow();
        if (_metadataCache.TryGetValue(clientId, out var cached) && cached.Until > now) return (cached.Client, null);

        try
        {
            var http = _http.CreateClient(HttpClientName);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (null, "Client metadata could not be loaded");
            if (response.Content.Headers.ContentLength is > MaxMetadataBytes) return (null, "Client metadata is too large");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var limited = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (limited.Length + read > MaxMetadataBytes) return (null, "Client metadata is too large");
                limited.Write(buffer, 0, read);
            }

            using var doc = JsonDocument.Parse(limited.ToArray());
            var root = doc.RootElement;
            if (!root.TryGetProperty("client_id", out var id) || id.GetString() != clientId) return (null, "Client metadata does not match the client_id");
            if (!root.TryGetProperty("redirect_uris", out var uris) || uris.ValueKind != JsonValueKind.Array) return (null, "Client metadata has no redirect_uris");

            var redirectUris = uris.EnumerateArray().Select(u => u.GetString()).OfType<string>().Take(10).ToArray();
            var name = root.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : url.Host;
            var client = new OAuthClient(clientId, Clip(name, 80), redirectUris);
            _metadataCache[clientId] = (client, now + CacheFor);
            return (client, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            return (null, "Client metadata could not be loaded");
        }
    }

    public static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
