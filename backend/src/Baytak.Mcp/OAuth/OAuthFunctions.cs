using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Baytak.Mcp.Api;
using Baytak.Mcp.Common;
using Baytak.Mcp.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.OAuth;

/// <summary>
/// An OAuth 2.1 authorization server in front of Baytak's own sign-in (phone number + 6-digit SMS code), as required for
/// ChatGPT apps and MCP clients: protected resource metadata (RFC 9728), authorization server metadata (RFC 8414),
/// dynamic client registration (RFC 7591) or Client ID Metadata Documents, authorization code with PKCE (S256), and rotating
/// refresh tokens. Access tokens are bound to this app's MCP resource and wrap the user's Baytak API session.
/// </summary>
public sealed class OAuthFunctions
{
    private readonly McpOptions _options;
    private readonly Sealer _sealer;
    private readonly ClientRegistry _clients;
    private readonly BaytakApi _api;
    private readonly IReplayStore _replay;
    private readonly TimeProvider _time;
    private readonly ILogger<OAuthFunctions> _logger;

    public OAuthFunctions(IOptions<McpOptions> options, Sealer sealer, ClientRegistry clients, BaytakApi api, IReplayStore replay,
        TimeProvider time, ILogger<OAuthFunctions> logger)
    {
        _options = options.Value;
        _sealer = sealer;
        _clients = clients;
        _api = api;
        _replay = replay;
        _time = time;
        _logger = logger;
    }

    // ------------------------------------------------------------------ discovery

    [Function("ProtectedResourceMetadata")]
    public IActionResult ProtectedResource(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/oauth-protected-resource")] HttpRequest req) =>
        Http.JsonBody(ProtectedResourceDocument());

    [Function("ProtectedResourceMetadataForMcp")]
    public IActionResult ProtectedResourceForMcp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/oauth-protected-resource/mcp")] HttpRequest req) =>
        Http.JsonBody(ProtectedResourceDocument());

    [Function("AuthorizationServerMetadata")]
    public IActionResult AuthorizationServer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/oauth-authorization-server")] HttpRequest req) =>
        Http.JsonBody(AuthorizationServerDocument());

    [Function("OpenIdConfiguration")]
    public IActionResult OpenIdConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/openid-configuration")] HttpRequest req) =>
        Http.JsonBody(AuthorizationServerDocument());

    /// <summary>Proves to OpenAI that this domain is ours when the app is submitted to the ChatGPT app directory.</summary>
    [Function("OpenAiAppsChallenge")]
    public IActionResult OpenAiChallenge(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/openai-apps-challenge")] HttpRequest req) =>
        string.IsNullOrWhiteSpace(_options.OpenAiChallenge)
            ? Http.Text(StatusCodes.Status404NotFound, "Not found")
            : Http.Text(StatusCodes.Status200OK, _options.OpenAiChallenge.Trim());

    private object ProtectedResourceDocument() => new Dictionary<string, object>
    {
        ["resource"] = _options.Resource,
        ["authorization_servers"] = new[] { _options.Issuer },
        ["scopes_supported"] = new[] { McpOptions.Scope },
        ["bearer_methods_supported"] = new[] { "header" },
        ["resource_name"] = "Baytak",
    };

    private object AuthorizationServerDocument() => new Dictionary<string, object>
    {
        ["issuer"] = _options.Issuer,
        ["authorization_endpoint"] = _options.Issuer + "/oauth/authorize",
        ["token_endpoint"] = _options.Issuer + "/oauth/token",
        ["registration_endpoint"] = _options.Issuer + "/oauth/register",
        ["scopes_supported"] = new[] { McpOptions.Scope },
        ["response_types_supported"] = new[] { "code" },
        ["response_modes_supported"] = new[] { "query" },
        ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
        ["code_challenge_methods_supported"] = new[] { "S256" },
        ["token_endpoint_auth_methods_supported"] = new[] { "none" },
        ["client_id_metadata_document_supported"] = true,
        ["authorization_response_iss_parameter_supported"] = true,
    };

    // ------------------------------------------------------------------ dynamic client registration

    [Function("OAuthRegister")]
    public async Task<IActionResult> Register(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "oauth/register")] HttpRequest req, CancellationToken ct)
    {
        if (req.ContentLength is > 16 * 1024) return Http.OAuthError("invalid_client_metadata", "Registration is too large");
        JsonElement body;
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct);
            body = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Http.OAuthError("invalid_client_metadata", "Registration must be a JSON object");
        }
        if (body.ValueKind != JsonValueKind.Object) return Http.OAuthError("invalid_client_metadata", "Registration must be a JSON object");

        if (!body.TryGetProperty("redirect_uris", out var uris) || uris.ValueKind != JsonValueKind.Array || uris.GetArrayLength() is 0 or > 5)
            return Http.OAuthError("invalid_redirect_uri", "Provide between 1 and 5 redirect_uris");
        var redirectUris = new List<string>();
        foreach (var u in uris.EnumerateArray())
        {
            var uri = u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            if (_clients.CheckRedirectUri(uri) is { } problem) return Http.OAuthError("invalid_redirect_uri", problem);
            redirectUris.Add(uri!);
        }

        var name = body.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
        if (name.Length == 0) name = new Uri(redirectUris[0]).Host;
        name = ClientRegistry.Clip(name, 80);

        var clientId = _clients.Register(name, redirectUris.ToArray());
        return Http.JsonBody(new Dictionary<string, object>
        {
            ["client_id"] = clientId,
            ["client_id_issued_at"] = _time.GetUtcNow().ToUnixTimeSeconds(),
            ["client_name"] = name,
            ["redirect_uris"] = redirectUris,
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none",
        }, StatusCodes.Status201Created);
    }

    // ------------------------------------------------------------------ authorization (the sign-in pages)

    [Function("OAuthAuthorize")]
    public async Task<IActionResult> Authorize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "oauth/authorize")] HttpRequest req, CancellationToken ct)
    {
        var q = req.Query;
        var (client, clientError) = await _clients.ResolveAsync(q["client_id"], ct);
        if (client is null) return Problem(req, clientError!);

        // Until the redirect URI is known to belong to the client, errors are shown here instead of being redirected.
        string redirectUri = q["redirect_uri"].ToString();
        if (redirectUri.Length == 0 && client.RedirectUris.Count == 1) redirectUri = client.RedirectUris[0];
        if (!client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal)) return Problem(req, "The redirect address does not match this client.");
        if (_clients.CheckRedirectUri(redirectUri) is { } redirectProblem) return Problem(req, redirectProblem);

        string? state = q.ContainsKey("state") ? q["state"].ToString() : null;
        if (q["response_type"] != "code") return RedirectError(req, redirectUri, state, "unsupported_response_type", "Only response_type=code is supported");
        var challenge = q["code_challenge"].ToString();
        if (q["code_challenge_method"] != "S256" || challenge.Length is < 43 or > 128)
            return RedirectError(req, redirectUri, state, "invalid_request", "PKCE with code_challenge_method=S256 is required");

        var resource = q["resource"].ToString();
        if (resource.Length > 0 && !SameResource(resource)) return RedirectError(req, redirectUri, state, "invalid_target", "Unknown resource");

        var request = new AuthRequest(client.ClientId, client.Name, redirectUri, state, challenge, McpOptions.Scope, _options.Resource);
        return PhonePage(req, request, NewState(request));
    }

    [Function("OAuthAuthorizeStep")]
    public async Task<IActionResult> AuthorizeStep(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "oauth/authorize")] HttpRequest req, CancellationToken ct)
    {
        if (!req.HasFormContentType) return Problem(req, "Please start the sign-in again.");
        var form = await req.ReadFormAsync(ct);
        var state = _sealer.Open<SignInState>(Purpose.SignIn, form["flow"]);
        if (state is null || state.Exp < Now()) return Problem(req, "This sign-in has expired. Please start again.");
        var request = state.Request;

        switch (form["step"].ToString())
        {
            case "send":
                return await SendCodeAsync(req, state, form["phone"].ToString().Trim(), ct);

            case "resend" when state.Phone is not null:
                return await SendCodeAsync(req, state, state.Phone, ct);

            case "restart":
                return PhonePage(req, request, NewState(request));

            case "verify" when state.Phone is not null:
                return await VerifyCodeAsync(req, state, form["code"].ToString().Trim(), ct);

            case "allow" when state.Jwt is not null:
            {
                var code = _sealer.Seal(Purpose.Code, new CodeGrant(Sealer.NewId(), request.ClientId, request.RedirectUri,
                    request.CodeChallenge, request.Scope, request.Resource, state.Jwt, Now() + _options.CodeSeconds));
                _logger.LogInformation("Authorization granted to client {Client}", request.ClientName);
                return Redirect(req, WithQuery(request.RedirectUri, ("code", code), ("state", request.State), ("iss", _options.Issuer)));
            }

            case "deny":
                return RedirectError(req, request.RedirectUri, request.State, "access_denied", "The user did not allow access");

            default:
                return Problem(req, "Please start the sign-in again.");
        }
    }

    private async Task<IActionResult> SendCodeAsync(HttpRequest req, SignInState state, string phone, CancellationToken ct)
    {
        if (phone.Length is 0 or > 32) return PhonePage(req, state.Request, Seal(state), "Please enter your phone number", phone);

        var reply = await _api.PostAsync("auth/login", null, new { phoneNumber = phone, status = "" }, ct);
        if (!reply.Ok) return PhonePage(req, state.Request, Seal(state), reply.Text, phone);

        var next = state with { Phone = phone, Exp = Now() + _options.SignInMinutes * 60 };
        return Http.Html(req, Pages.Code(Seal(next), phone, notice: state.Phone == phone ? "A new code is on its way." : null));
    }

    private async Task<IActionResult> VerifyCodeAsync(HttpRequest req, SignInState state, string code, CancellationToken ct)
    {
        if (code.Length is 0 or > 12) return Http.Html(req, Pages.Code(Seal(state), state.Phone!, "Enter the 6-digit code"));

        var reply = await _api.PostAsync("auth/validate", null, new { phoneNumber = state.Phone, pincode = code }, ct);
        if (!reply.Ok) return Http.Html(req, Pages.Code(Seal(state), state.Phone!, reply.Text));

        var jwt = reply.Json().GetProperty("auth").GetString()!;
        var status = await _api.GetAsync("user/status", jwt, ct);
        var role = status.Ok ? status.Json().GetProperty("role").GetString() : null;
        var userStatus = status.Ok ? status.Json().GetProperty("userStatus").GetString() : null;

        var next = state with { Jwt = jwt, Role = role, Status = userStatus, Exp = Now() + _options.SignInMinutes * 60 };
        var isAdmin = role == "Admin" && userStatus == "Approved";
        var pending = role == "Admin" && userStatus == "Pending";
        return Http.Html(req, Pages.Consent(Seal(next), state.Request.ClientName, RedirectHost(state.Request), state.Phone!, isAdmin, pending),
            RedirectOrigin(state.Request));
    }

    // ------------------------------------------------------------------ token endpoint

    [Function("OAuthToken")]
    public async Task<IActionResult> Token(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "oauth/token")] HttpRequest req, CancellationToken ct)
    {
        if (!req.HasFormContentType) return Http.OAuthError("invalid_request", "Use application/x-www-form-urlencoded");
        var form = await req.ReadFormAsync(ct);
        var clientId = form["client_id"].ToString();
        var resource = form["resource"].ToString();
        if (resource.Length > 0 && !SameResource(resource)) return Http.OAuthError("invalid_target", "Unknown resource");

        return form["grant_type"].ToString() switch
        {
            "authorization_code" => await RedeemCodeAsync(form["code"], clientId, form["redirect_uri"], form["code_verifier"], ct),
            "refresh_token" => await RefreshAsync(form["refresh_token"], clientId, ct),
            _ => Http.OAuthError("unsupported_grant_type", "Use authorization_code or refresh_token"),
        };
    }

    private async Task<IActionResult> RedeemCodeAsync(string? code, string clientId, string? redirectUri, string? verifier, CancellationToken ct)
    {
        var grant = _sealer.Open<CodeGrant>(Purpose.Code, code);
        if (grant is null || grant.Exp < Now()) return Http.OAuthError("invalid_grant", "The authorization code is invalid or expired");
        if (grant.ClientId != clientId) return Http.OAuthError("invalid_grant", "The code was issued to another client");
        if (!string.IsNullOrEmpty(redirectUri) && redirectUri != grant.RedirectUri) return Http.OAuthError("invalid_grant", "redirect_uri does not match");
        if (verifier is null || verifier.Length is < 43 or > 128 || !PkceMatches(verifier, grant.CodeChallenge))
            return Http.OAuthError("invalid_grant", "PKCE verification failed");
        if (!await _replay.TryUseAsync(Purpose.Code, grant.Jti, DateTimeOffset.FromUnixTimeSeconds(grant.Exp), ct))
            return Http.OAuthError("invalid_grant", "The authorization code was already used");

        return Tokens(grant.ClientId, grant.Scope, grant.Resource, grant.Jwt, Sealer.NewId());
    }

    private async Task<IActionResult> RefreshAsync(string? token, string clientId, CancellationToken ct)
    {
        var grant = _sealer.Open<RefreshGrant>(Purpose.Refresh, token);
        if (grant is null || grant.Exp < Now()) return Http.OAuthError("invalid_grant", "The refresh token is invalid or expired");
        if (grant.ClientId != clientId) return Http.OAuthError("invalid_grant", "The refresh token was issued to another client");
        if (await _replay.IsRevokedAsync(grant.Family, ct)) return Http.OAuthError("invalid_grant", "The refresh token was revoked");

        if (!await _replay.TryUseAsync(Purpose.Refresh, grant.Jti, DateTimeOffset.FromUnixTimeSeconds(grant.Exp), ct))
        {
            // A rotated refresh token came back: someone else may hold this session. End the whole family.
            await _replay.RevokeAsync(grant.Family, DateTimeOffset.FromUnixTimeSeconds(grant.Exp), ct);
            _logger.LogWarning("Refresh token reuse detected; family revoked");
            return Http.OAuthError("invalid_grant", "The refresh token was already used");
        }

        return Tokens(grant.ClientId, grant.Scope, grant.Resource, grant.Jwt, grant.Family);
    }

    private IActionResult Tokens(string clientId, string scope, string resource, string jwt, string family)
    {
        var now = Now();
        var sessionEnds = ApiJwt.ExpiresAt(jwt) ?? now + 3600;
        if (sessionEnds <= now) return Http.OAuthError("invalid_grant", "The Baytak session has ended. Please sign in again.");

        var accessExp = Math.Min(now + _options.AccessTokenMinutes * 60, sessionEnds);
        var access = _sealer.Seal(Purpose.Access, new AccessGrant(clientId, scope, resource, jwt, accessExp));
        var refresh = _sealer.Seal(Purpose.Refresh, new RefreshGrant(Sealer.NewId(), family, clientId, scope, resource, jwt, sessionEnds));

        return Http.JsonBody(new Dictionary<string, object>
        {
            ["access_token"] = access,
            ["token_type"] = "Bearer",
            ["expires_in"] = accessExp - now,
            ["refresh_token"] = refresh,
            ["scope"] = scope,
        });
    }

    // ------------------------------------------------------------------ logo

    [Function("OAuthLogo")]
    public IActionResult Logo([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "oauth/logo.png")] HttpRequest req)
    {
        using var stream = typeof(OAuthFunctions).Assembly.GetManifestResourceStream("Baytak.Mcp.logo.png");
        if (stream is null) return new NotFoundResult();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        Http.SetHeader(req, "Cache-Control", "public, max-age=86400");
        return new BytesResult(copy.ToArray(), "image/png");
    }

    // ------------------------------------------------------------------ helpers

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();

    private string NewState(AuthRequest request) => Seal(new SignInState(request, Now() + _options.SignInMinutes * 60));

    private string Seal(SignInState state) => _sealer.Seal(Purpose.SignIn, state);

    private IActionResult PhonePage(HttpRequest req, AuthRequest request, string flow, string? error = null, string? phone = null) =>
        Http.Html(req, Pages.Phone(flow, request.ClientName, RedirectHost(request), error, phone));

    private static IActionResult Problem(HttpRequest req, string message) => Http.Html(req, Pages.Problem(message), status: StatusCodes.Status400BadRequest);

    private IActionResult RedirectError(HttpRequest req, string redirectUri, string? state, string error, string description) =>
        Redirect(req, WithQuery(redirectUri, ("error", error), ("error_description", description), ("state", state), ("iss", _options.Issuer)));

    /// <summary>A 302 to the client. Built by hand so it does not depend on MVC result executors in the Functions host.</summary>
    private static IActionResult Redirect(HttpRequest req, string location)
    {
        Http.SetHeader(req, "Location", location);
        return new ContentResult { StatusCode = StatusCodes.Status302Found, Content = "", ContentType = "text/plain; charset=utf-8" };
    }

    private bool SameResource(string resource) =>
        string.Equals(resource.TrimEnd('/'), _options.Resource, StringComparison.Ordinal) ||
        string.Equals(resource.TrimEnd('/'), _options.Issuer, StringComparison.Ordinal);

    private static string RedirectHost(AuthRequest request) => new Uri(request.RedirectUri).Host;

    private static string RedirectOrigin(AuthRequest request) => new Uri(request.RedirectUri).GetLeftPart(UriPartial.Authority);

    private static bool PkceMatches(string verifier, string challenge)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var expected = System.Buffers.Text.Base64Url.EncodeToString(hash);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(challenge));
    }

    private static string WithQuery(string uri, params (string Key, string? Value)[] values)
    {
        var builder = new StringBuilder(uri);
        var separator = uri.Contains('?') ? '&' : '?';
        foreach (var (key, value) in values)
        {
            if (value is null) continue;
            builder.Append(separator).Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }
        return builder.ToString();
    }
}
