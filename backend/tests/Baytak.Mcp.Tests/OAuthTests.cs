using System.Text.Json;
using Baytak.Api.Functions;
using Baytak.Mcp.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.Tests;

/// <summary>The sign-in, as ChatGPT runs it: discovery, registration, the SMS code pages, and tokens.</summary>
public class OAuthTests
{
    [Fact]
    public async Task Discovery_documents_point_at_this_app()
    {
        using var host = new McpHost();
        var resource = await host.Get<OAuthFunctions>((f, r) => Task.FromResult(f.ProtectedResourceForMcp(r)));
        Assert.Equal(McpHost.Resource, resource.Json.GetProperty("resource").GetString());
        Assert.Equal(McpHost.Base, resource.Json.GetProperty("authorization_servers")[0].GetString());

        var server = await host.Get<OAuthFunctions>((f, r) => Task.FromResult(f.AuthorizationServer(r)));
        var doc = server.Json;
        Assert.Equal(McpHost.Base, doc.GetProperty("issuer").GetString());
        Assert.Equal(McpHost.Base + "/oauth/authorize", doc.GetProperty("authorization_endpoint").GetString());
        Assert.Equal(McpHost.Base + "/oauth/token", doc.GetProperty("token_endpoint").GetString());
        Assert.Equal(McpHost.Base + "/oauth/register", doc.GetProperty("registration_endpoint").GetString());
        Assert.Equal("S256", doc.GetProperty("code_challenge_methods_supported")[0].GetString());
        Assert.True(doc.GetProperty("client_id_metadata_document_supported").GetBoolean());
    }

    [Fact]
    public async Task The_openai_domain_challenge_is_served_only_when_configured()
    {
        using var host = new McpHost();
        var none = await host.Get<OAuthFunctions>((f, r) => Task.FromResult(f.OpenAiChallenge(r)));
        Assert.Equal(404, none.Status);

        host.Services.GetRequiredService<IOptions<McpOptions>>().Value.OpenAiChallenge = " token-from-the-dashboard ";
        var served = await host.Get<OAuthFunctions>((f, r) => Task.FromResult(f.OpenAiChallenge(r)));
        Assert.Equal(200, served.Status);
        Assert.Equal("token-from-the-dashboard", served.Body);
    }

    [Fact]
    public async Task Mcp_without_a_token_answers_401_with_the_metadata_location()
    {
        using var host = new McpHost();
        var reply = await host.Mcp(null, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { } });
        Assert.Equal(401, reply.Status);
        Assert.Contains($"resource_metadata=\"{McpHost.Base}/.well-known/oauth-protected-resource\"", reply.Header("WWW-Authenticate"));
        Assert.DoesNotContain("error=", reply.Header("WWW-Authenticate")); // no token at all: just where to sign in (RFC 6750)

        var bad = await host.Mcp("not-a-token", new { jsonrpc = "2.0", id = 2, method = "ping" });
        Assert.Equal(401, bad.Status);
        Assert.Contains("error=\"invalid_token\"", bad.Header("WWW-Authenticate"));
    }

    [Fact]
    public async Task Registration_only_accepts_known_redirect_hosts()
    {
        using var host = new McpHost();
        var evil = await host.PostJson<OAuthFunctions>((f, r) => f.Register(r, CancellationToken.None),
            JsonSerializer.Serialize(new { client_name = "ChatGPT", redirect_uris = new[] { "https://evil.example/callback" } }));
        Assert.Equal(400, evil.Status);
        Assert.Equal("invalid_redirect_uri", evil.Json.GetProperty("error").GetString());

        var plainHttp = await host.PostJson<OAuthFunctions>((f, r) => f.Register(r, CancellationToken.None),
            JsonSerializer.Serialize(new { redirect_uris = new[] { "http://chatgpt.com/callback" } }));
        Assert.Equal(400, plainHttp.Status);

        Assert.StartsWith("bk_", await host.RegisterClientAsync());
        Assert.StartsWith("bk_", await host.RegisterClientAsync("http://localhost:6274/oauth/callback")); // MCP Inspector
    }

    [Fact]
    public async Task Signing_in_with_the_sms_code_yields_tokens_and_the_code_works_once()
    {
        using var host = new McpHost();
        var clientId = await host.RegisterClientAsync();
        var (verifier, challenge) = McpHost.Pkce();
        var code = await host.SignInAsync(McpHost.ResidentPhone, clientId, challenge);

        var first = await host.RedeemAsync(clientId, code, verifier);
        Assert.Equal(200, first.Status);
        Assert.Equal("Bearer", first.Json.GetProperty("token_type").GetString());
        Assert.Equal(3600, first.Json.GetProperty("expires_in").GetInt64());

        var again = await host.RedeemAsync(clientId, code, verifier);
        Assert.Equal("invalid_grant", again.Json.GetProperty("error").GetString());

        // The phone app's session is untouched: signing in for ChatGPT does not sign anyone out.
        var token = first.Json.GetProperty("access_token").GetString()!;
        Assert.Equal(200, (await host.Mcp(token, new { jsonrpc = "2.0", id = 1, method = "ping" })).Status);
    }

    [Fact]
    public async Task Pkce_client_and_redirect_must_match()
    {
        using var host = new McpHost();
        var clientId = await host.RegisterClientAsync();
        var other = await host.RegisterClientAsync();
        var (verifier, challenge) = McpHost.Pkce();
        var code = await host.SignInAsync(McpHost.ResidentPhone, clientId, challenge);

        Assert.Equal("invalid_grant", (await host.RedeemAsync(clientId, code, McpHost.Pkce().Verifier)).Json.GetProperty("error").GetString());
        Assert.Equal("invalid_grant", (await host.RedeemAsync(other, code, verifier)).Json.GetProperty("error").GetString());
        Assert.Equal("invalid_grant", (await host.RedeemAsync(clientId, code, verifier, "https://chatgpt.com/other")).Json.GetProperty("error").GetString());
        Assert.Equal(200, (await host.RedeemAsync(clientId, code, verifier)).Status); // still unused, so it works once
    }

    [Fact]
    public async Task Authorize_refuses_unknown_clients_and_mismatched_redirects()
    {
        using var host = new McpHost();
        var (_, challenge) = McpHost.Pkce();

        var unknown = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None), McpHost.AuthorizeQuery("bk_forged", challenge));
        Assert.Equal(400, unknown.Status);
        Assert.Contains("can't be used", unknown.Body);

        var clientId = await host.RegisterClientAsync();
        var wrongRedirect = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None),
            McpHost.AuthorizeQuery(clientId, challenge, "https://chatgpt.com/somewhere-else"));
        Assert.Equal(400, wrongRedirect.Status);

        var noPkce = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None),
            McpHost.AuthorizeQuery(clientId, challenge).Replace("code_challenge_method=S256", "code_challenge_method=plain"));
        Assert.Equal(302, noPkce.Status);
        Assert.Contains("error=invalid_request", noPkce.Header("Location"));
    }

    [Fact]
    public async Task A_wrong_code_shows_the_apis_message_and_cancel_returns_access_denied()
    {
        using var host = new McpHost();
        var clientId = await host.RegisterClientAsync();
        var (_, challenge) = McpHost.Pkce();
        host.Api.Time.Advance(TimeSpan.FromSeconds(31));
        var page = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None), McpHost.AuthorizeQuery(clientId, challenge));
        var sent = await host.PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", McpHost.Flow(page)), ("step", "send"), ("phone", McpHost.ResidentPhone));

        var wrong = await host.PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", McpHost.Flow(sent)), ("step", "verify"), ("code", "000000"));
        Assert.Contains("That code is not correct.", wrong.Body);

        var code = host.Api.Sms.LastCodeFor(Baytak.Api.Tests.TestApp.PhoneNumberFor(McpHost.ResidentPhone));
        var consent = await host.PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", McpHost.Flow(wrong)), ("step", "verify"), ("code", code));
        var denied = await host.PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", McpHost.Flow(consent)), ("step", "deny"));
        Assert.Equal(302, denied.Status);
        Assert.Contains("error=access_denied", denied.Header("Location"));
        Assert.Contains("state=xyz", denied.Header("Location"));
    }

    [Fact]
    public async Task A_tampered_or_expired_sign_in_cannot_continue()
    {
        using var host = new McpHost();
        var clientId = await host.RegisterClientAsync();
        var (_, challenge) = McpHost.Pkce();
        var page = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None), McpHost.AuthorizeQuery(clientId, challenge));
        var flow = McpHost.Flow(page);

        var tampered = await host.PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", flow[..^4] + "AAAA"), ("step", "allow"));
        Assert.Equal(400, tampered.Status);

        // Skipping straight to "allow" without a verified code is refused too.
        var skipped = await host.PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", flow), ("step", "allow"));
        Assert.Equal(400, skipped.Status);

        host.Api.Time.Advance(TimeSpan.FromMinutes(16));
        var expired = await host.PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", flow), ("step", "send"), ("phone", McpHost.ResidentPhone));
        Assert.Contains("expired", expired.Body);
    }

    [Fact]
    public async Task Refresh_tokens_rotate_and_a_reused_one_ends_the_session()
    {
        using var host = new McpHost();
        var connection = await host.ConnectAsync(McpHost.ResidentPhone);

        var first = await host.RefreshAsync(connection.ClientId, connection.RefreshToken);
        Assert.Equal(200, first.Status);
        var rotated = first.Json.GetProperty("refresh_token").GetString()!;
        Assert.NotEqual(connection.RefreshToken, rotated);

        // The old refresh token comes back (stolen copy): refused, and the rotated one dies with it.
        Assert.Equal("invalid_grant", (await host.RefreshAsync(connection.ClientId, connection.RefreshToken)).Json.GetProperty("error").GetString());
        Assert.Equal("invalid_grant", (await host.RefreshAsync(connection.ClientId, rotated)).Json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Access_tokens_expire_and_signing_out_in_the_app_ends_the_connection()
    {
        using var host = new McpHost();
        var (_, resident, _) = await host.SeedBuildingAsync();
        var connection = await host.ConnectAsync(McpHost.ResidentPhone);
        Assert.Equal(200, (await host.Mcp(connection.AccessToken, new { jsonrpc = "2.0", id = 1, method = "tools/list" })).Status);

        // Sign out in the phone app: every session of the account ends, ChatGPT's included.
        Assert.True((await host.Api.Call<UserFunctions>((f, r, ct) => f.Logout(r, ct), new { }, resident)).Ok);
        var after = await host.Mcp(connection.AccessToken, new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        Assert.Equal(401, after.Status);
        Assert.Contains("invalid_token", after.Header("WWW-Authenticate"));

        var fresh = await host.ConnectAsync(McpHost.ResidentPhone);
        host.Api.Time.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(401, (await host.Mcp(fresh.AccessToken, new { jsonrpc = "2.0", id = 3, method = "ping" })).Status);
        var refreshed = await host.RefreshAsync(fresh.ClientId, fresh.RefreshToken);
        Assert.Equal(200, (await host.Mcp(refreshed.Json.GetProperty("access_token").GetString(), new { jsonrpc = "2.0", id = 4, method = "ping" })).Status);
    }

    [Fact]
    public async Task Client_id_metadata_documents_are_accepted_from_allowed_hosts_only()
    {
        using var host = new McpHost();
        const string cimd = "https://chatgpt.com/oauth/client-metadata.json";
        host.ClientMetadata[cimd] = JsonSerializer.Serialize(new { client_id = cimd, client_name = "ChatGPT", redirect_uris = new[] { McpHost.Redirect } });
        var (_, challenge) = McpHost.Pkce();

        var ok = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None), McpHost.AuthorizeQuery(cimd, challenge));
        Assert.Equal(200, ok.Status);
        Assert.Contains("ChatGPT", ok.Body);

        const string elsewhere = "https://metadata.example/client.json";
        host.ClientMetadata[elsewhere] = JsonSerializer.Serialize(new { client_id = elsewhere, redirect_uris = new[] { McpHost.Redirect } });
        var refused = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None), McpHost.AuthorizeQuery(elsewhere, challenge));
        Assert.Equal(400, refused.Status);
    }

    [Fact]
    public async Task An_unreachable_api_is_explained_on_the_page_and_in_tools()
    {
        using var host = new McpHost();
        var connection = await host.ConnectAsync(McpHost.ResidentPhone);
        host.ApiDown = true;

        var clientId = await host.RegisterClientAsync();
        var page = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None), McpHost.AuthorizeQuery(clientId, McpHost.Pkce().Challenge));
        var sent = await host.PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", McpHost.Flow(page)), ("step", "send"), ("phone", McpHost.ResidentPhone));
        Assert.Equal(200, sent.Status);
        Assert.Contains("We couldn&#39;t reach Baytak right now.", sent.Body);

        var balance = await host.CallToolAsync(connection.AccessToken, "get_my_balance");
        Assert.True(McpHost.IsError(balance));
        Assert.Equal("We couldn't reach Baytak right now. Please try again in a moment.", McpHost.Text(balance));
        Assert.Equal(200, (await host.Mcp(connection.AccessToken, new { jsonrpc = "2.0", id = 5, method = "tools/list" })).Status);
    }

    [Fact]
    public async Task Pages_are_locked_down_and_show_the_app_logo()
    {
        using var host = new McpHost();
        var clientId = await host.RegisterClientAsync();
        var page = await host.Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None), McpHost.AuthorizeQuery(clientId, McpHost.Pkce().Challenge));
        Assert.Contains("frame-ancestors 'none'", page.Header("Content-Security-Policy"));
        Assert.Contains("src=\"/oauth/logo.png\"", page.Body);

        var logo = await host.Get<OAuthFunctions>((f, r) => Task.FromResult(f.Logo(r)));
        var png = Convert.FromBase64String(logo.Body);
        var original = await File.ReadAllBytesAsync(Path.Combine(RepoRoot(), "assets", "adaptive-icon.png"));
        Assert.Equal(original, png); // the project's own logo file, byte for byte
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "app.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
