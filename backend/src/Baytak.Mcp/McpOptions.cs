namespace Baytak.Mcp;

/// <summary>Settings for the MCP app (configuration section "Mcp", env vars Mcp__*).</summary>
public sealed class McpOptions
{
    /// <summary>Public origin of this app, for example https://baytak-mcp-x.azurewebsites.net. Never taken from request headers.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Base64 256-bit key that seals sign-in state, codes, tokens and client ids. Rotating it disconnects every client.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Optional previous key, still accepted when opening sealed values, so a rotation can be gradual.</summary>
    public string? PreviousSecretKey { get; set; }

    /// <summary>Base URL of the Baytak API, ending in /api.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Azure Functions host key of the Baytak API, sent as x-functions-key.</summary>
    public string? ApiKey { get; set; }

    public int AccessTokenMinutes { get; set; } = 60;
    public int CodeSeconds { get; set; } = 120;
    public int SignInMinutes { get; set; } = 15;

    /// <summary>Hosts allowed in OAuth redirect URIs and in the Origin header of MCP requests (subdomains included).</summary>
    public string RedirectHosts { get; set; } = "chatgpt.com,claude.ai,localhost,127.0.0.1";

    /// <summary>Hosts allowed to serve Client ID Metadata Documents (client_id given as an https URL).</summary>
    public string ClientMetadataHosts { get; set; } = "chatgpt.com,openai.com,claude.ai,anthropic.com";

    /// <summary>The domain-verification token OpenAI shows in its dashboard, served at /.well-known/openai-apps-challenge. Empty: the page answers 404.</summary>
    public string? OpenAiChallenge { get; set; }

    /// <summary>"Table" (Azure Table Storage, production) or "Memory" (development and tests only).</summary>
    public string ReplayStore { get; set; } = "Table";

    public string Issuer => PublicBaseUrl.TrimEnd('/');
    public string Resource => Issuer + "/mcp";
    public string ResourceMetadataUrl => Issuer + "/.well-known/oauth-protected-resource";

    public const string Scope = "baytak";

    /// <summary>True when <paramref name="host"/> is one of the comma-separated hosts or a subdomain of one.</summary>
    public static bool HostAllowed(string host, string list) =>
        list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
}
