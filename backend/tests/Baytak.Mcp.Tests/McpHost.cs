using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Baytak.Api.Functions;
using Baytak.Api.Tests;
using Baytak.Mcp;
using Baytak.Mcp.Api;
using Baytak.Mcp.Common;
using Baytak.Mcp.Mcp;
using Baytak.Mcp.OAuth;
using Baytak.Mcp.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Baytak.Mcp.Tests;

public sealed record Reply(int Status, string Body, IHeaderDictionary Headers)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v.ToString() : null;
}

/// <summary>
/// Hosts the MCP app's function classes against the real Baytak API, which runs in memory through the API's own test
/// harness. Calls the MCP app makes to the API go through <see cref="ApiBridge"/> instead of the network.
/// </summary>
public sealed class McpHost : IDisposable
{
    public const string Base = "https://mcp.test";
    public const string Resource = Base + "/mcp";
    public const string Redirect = "https://chatgpt.com/connector_platform_oauth_redirect";

    public TestApp Api { get; } = new();
    public ServiceProvider Services { get; }
    public Dictionary<string, string> ClientMetadata { get; } = new();

    /// <summary>Makes every call to the API fail as if it were unreachable.</summary>
    public bool ApiDown { get; set; }

    public McpHost()
    {
        Api.Time.SetUtcNow(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero)); // the receipts in these tests are from October 2026
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<McpOptions>(o =>
        {
            o.PublicBaseUrl = Base;
            o.SecretKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            o.ApiBaseUrl = "https://api.test/api";
            o.ApiKey = "test-host-key";
            o.ReplayStore = "Memory";
        });
        services.AddSingleton<TimeProvider>(Api.Time);
        services.AddSingleton<IHttpClientFactory>(new Factory(this));
        services.AddSingleton<Sealer>();
        services.AddSingleton<ClientRegistry>();
        services.AddSingleton<BaytakApi>();
        services.AddSingleton<BaytakTools>();
        services.AddSingleton<IReplayStore, MemoryReplayStore>();
        Services = services.BuildServiceProvider();
    }

    // ------------------------------------------------------------------ invoking functions

    public async Task<Reply> Invoke<T>(Func<T, HttpRequest, Task<IActionResult>> handler, Action<HttpRequest> build) where T : class
    {
        using var scope = Services.CreateScope();
        var function = ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        build(context.Request);

        var result = await handler(function, context.Request);
        return result switch
        {
            ContentResult c => new Reply(c.StatusCode ?? 200, c.Content ?? "", context.Response.Headers),
            StatusCodeResult s => new Reply(s.StatusCode, "", context.Response.Headers),
            BytesResult b => new Reply(200, Convert.ToBase64String(b.Bytes), context.Response.Headers),
            _ => throw new InvalidOperationException($"Unexpected result {result.GetType().Name}"),
        };
    }

    public Task<Reply> Get<T>(Func<T, HttpRequest, Task<IActionResult>> handler, string query = "") where T : class =>
        Invoke(handler, r => r.QueryString = new QueryString(query));

    public Task<Reply> PostForm<T>(Func<T, HttpRequest, Task<IActionResult>> handler, params (string Key, string Value)[] fields) where T : class =>
        Invoke(handler, r =>
        {
            var body = string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
            SetBody(r, body, "application/x-www-form-urlencoded");
        });

    public Task<Reply> PostJson<T>(Func<T, HttpRequest, Task<IActionResult>> handler, string json, string? token = null, string? origin = null) where T : class =>
        Invoke(handler, r =>
        {
            SetBody(r, json, "application/json");
            if (token is not null) r.Headers.Authorization = "Bearer " + token;
            if (origin is not null) r.Headers.Origin = origin;
        });

    private static void SetBody(HttpRequest r, string body, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        r.Method = "POST";
        r.ContentType = contentType;
        r.ContentLength = bytes.Length;
        r.Body = new MemoryStream(bytes);
    }

    // ------------------------------------------------------------------ OAuth, as ChatGPT does it

    public async Task<string> RegisterClientAsync(string redirect = Redirect)
    {
        var reply = await PostJson<OAuthFunctions>((f, r) => f.Register(r, CancellationToken.None),
            JsonSerializer.Serialize(new { client_name = "ChatGPT", redirect_uris = new[] { redirect } }));
        Assert.Equal(201, reply.Status);
        return reply.Json.GetProperty("client_id").GetString()!;
    }

    public sealed record Connection(string ClientId, string AccessToken, string RefreshToken, string Verifier);

    /// <summary>Registers a client, signs in through the pages with the SMS code, allows access and redeems the code.</summary>
    public async Task<Connection> ConnectAsync(string phone, string? clientId = null)
    {
        clientId ??= await RegisterClientAsync();
        var (verifier, challenge) = Pkce();
        var code = await SignInAsync(phone, clientId, challenge);
        var tokens = await RedeemAsync(clientId, code, verifier);
        Assert.Equal(200, tokens.Status);
        return new Connection(clientId, tokens.Json.GetProperty("access_token").GetString()!, tokens.Json.GetProperty("refresh_token").GetString()!, verifier);
    }

    public async Task<string> SignInAsync(string phone, string clientId, string challenge)
    {
        Api.Time.Advance(TimeSpan.FromSeconds(31)); // clear the API's resend cool-down
        var page = await Get<OAuthFunctions>((f, r) => f.Authorize(r, CancellationToken.None), AuthorizeQuery(clientId, challenge));
        Assert.Equal(200, page.Status);

        var sent = await PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", Flow(page)), ("step", "send"), ("phone", phone));
        Assert.Contains("Enter verification code", sent.Body);

        var smsCode = Api.Sms.LastCodeFor(TestApp.PhoneNumberFor(phone));
        var consent = await PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", Flow(sent)), ("step", "verify"), ("code", smsCode));
        Assert.Contains("to use Baytak?", consent.Body);

        var allowed = await PostForm<OAuthFunctions>((f, r) => f.AuthorizeStep(r, CancellationToken.None), ("flow", Flow(consent)), ("step", "allow"));
        Assert.Equal(302, allowed.Status);
        var location = new Uri(allowed.Header("Location")!);
        Assert.StartsWith(Redirect, location.ToString());
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("xyz", query["state"].ToString());
        Assert.Equal(Base, query["iss"].ToString());
        return query["code"].ToString();
    }

    public Task<Reply> RedeemAsync(string clientId, string code, string verifier, string redirect = Redirect) =>
        PostForm<OAuthFunctions>((f, r) => f.Token(r, CancellationToken.None),
            ("grant_type", "authorization_code"), ("code", code), ("client_id", clientId), ("redirect_uri", redirect),
            ("code_verifier", verifier), ("resource", Resource));

    public Task<Reply> RefreshAsync(string clientId, string refreshToken) =>
        PostForm<OAuthFunctions>((f, r) => f.Token(r, CancellationToken.None),
            ("grant_type", "refresh_token"), ("refresh_token", refreshToken), ("client_id", clientId), ("resource", Resource));

    public static string AuthorizeQuery(string clientId, string challenge, string redirect = Redirect) =>
        $"?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirect)}" +
        $"&code_challenge={challenge}&code_challenge_method=S256&state=xyz&scope=baytak&resource={Uri.EscapeDataString(Resource)}";

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(48));
        return (verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string Flow(Reply page) =>
        WebUtility.HtmlDecode(Regex.Match(page.Body, "name=\"flow\" value=\"([^\"]+)\"").Groups[1].Value);

    // ------------------------------------------------------------------ MCP

    private int _id;

    public Task<Reply> Mcp(string? token, object message, string? origin = null) =>
        PostJson<McpFunction>((f, r) => f.Run(r, CancellationToken.None), JsonSerializer.Serialize(message), token, origin);

    public async Task<JsonElement> RpcAsync(string token, string method, object? parameters = null)
    {
        var reply = await Mcp(token, new { jsonrpc = "2.0", id = ++_id, method, @params = parameters ?? new { } });
        Assert.Equal(200, reply.Status);
        Assert.False(reply.Json.TryGetProperty("error", out var error), error.ToString());
        return reply.Json.GetProperty("result");
    }

    public Task<JsonElement> CallToolAsync(string token, string name, object? arguments = null) =>
        RpcAsync(token, "tools/call", new { name, arguments = arguments ?? new { } });

    public static string Text(JsonElement toolResult) => toolResult.GetProperty("content")[0].GetProperty("text").GetString()!;

    public static bool IsError(JsonElement toolResult) => toolResult.TryGetProperty("isError", out var e) && e.GetBoolean();

    // ------------------------------------------------------------------ a building to work with

    public const string AdminPhone = "03 111 111";
    public const string ResidentPhone = "70 222 222";
    public const string NeighbourPhone = "71 333 333";

    /// <summary>
    /// What the app would have done: an approved admin with a building and a receipt format, two approved residents
    /// (3A and 5B), and one receipt of 150.5 USD issued to both. Returns the API tokens.
    /// </summary>
    public async Task<(string Admin, string Resident, string Neighbour)> SeedBuildingAsync()
    {
        var admin = await Api.LoginAsApprovedAdminAsync(AdminPhone);
        Assert.True((await Api.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), new
        {
            buildingNumber = "12", buildingName = "Cedar Residence", street = "Hamra St", city = "Beirut", country = "Lebanon",
            numberOfFloors = 8, latitude = 33.8938, longitude = 35.5018, linkedAppartments = 0,
        }, admin)).Ok);
        Assert.True((await Api.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), new
        {
            currency = "USD",
            customFields = new object[]
            {
                new { fieldName = "Maintenance", type = "Amount", value = "" },
                new { fieldName = "Electricity", type = "Amount", value = "" },
                new { fieldName = "Note", type = "Text", value = "" },
            },
            Date = "",
        }, admin)).Ok);

        var buildings = await Api.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: admin);
        var buildingId = buildings.Json[0].GetProperty("value").GetInt32();

        var resident = await JoinAsync(ResidentPhone, "3A", "Sea view", 3, buildingId, admin, "ExponentPushToken[resident-3a-token]");
        var neighbour = await JoinAsync(NeighbourPhone, "5B", "Corner", 5, buildingId, admin, "ExponentPushToken[neighbour-5b-token]");

        var add = await Api.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), token: admin, rawBody: """
            {"currency":"USD","appartmentIds":"","version":"v2.0",
             "receipt":{"currency":"USD","date":"2026-10-01",
               "customFields":[
                 {"fieldName":"Maintenance","type":"Amount","value":"100"},
                 {"fieldName":"Electricity","type":"Amount","value":"50.5"},
                 {"fieldName":"Note","type":"Text","value":"Pay by the 10th"}]}}
            """);
        Assert.True(add.Ok, add.Body);
        return (admin, resident, neighbour);
    }

    private async Task<string> JoinAsync(string phone, string number, string description, int floor, int buildingId, string admin, string pushToken)
    {
        var token = await Api.LoginAsync(phone);
        Assert.True((await Api.Call<UserFunctions>((f, r, ct) => f.SetupNotification(r, ct), new { notificationToken = pushToken }, token)).Ok);
        var join = await Api.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct),
            new { appartmentNumber = number, appartmentDescription = description, floor, buildingId }, token);
        Assert.True(join.Ok, join.Body);
        var pending = await Api.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin);
        var id = pending.Json.EnumerateArray().Single(a => a.GetProperty("appartmentNumber").GetString() == number).GetProperty("id").GetInt32();
        Assert.True((await Api.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id }, admin)).Ok);
        return token;
    }

    public void Dispose()
    {
        Services.Dispose();
        Api.Dispose();
    }

    // ------------------------------------------------------------------ plumbing

    private sealed class Factory(McpHost host) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => name switch
        {
            BaytakApi.HttpClientName => new HttpClient(new ApiBridge(host), disposeHandler: true),
            ClientRegistry.HttpClientName => new HttpClient(new MetadataHandler(host.ClientMetadata), disposeHandler: true),
            _ => throw new InvalidOperationException(name),
        };
    }

    private sealed class MetadataHandler(Dictionary<string, string> documents) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(documents.TryGetValue(request.RequestUri!.ToString(), out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>Routes the MCP app's HTTP calls to the API function classes, in memory.</summary>
    private sealed class ApiBridge(McpHost host) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (host.ApiDown) throw new HttpRequestException("No connection could be made");
            var api = host.Api;
            Assert.Equal("test-host-key", request.Headers.GetValues("x-functions-key").Single());
            var path = request.RequestUri!.AbsolutePath;
            Assert.StartsWith("/api/", path);
            path = path["/api/".Length..];
            var query = request.RequestUri.Query.Length > 0 ? request.RequestUri.Query : null;
            var token = request.Headers.Authorization?.Parameter;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            Response? reply = (request.Method.Method, path) switch
            {
                ("POST", "auth/login") => await api.Call<AuthFunctions>((f, r, c) => f.Login(r, c), token: token, rawBody: body),
                ("POST", "auth/validate") => await api.Call<AuthFunctions>((f, r, c) => f.Validate(r, c), token: token, rawBody: body),
                ("GET", "user/status") => await api.Call<UserFunctions>((f, r, c) => f.Status(r, c), token: token),
                ("GET", "appartment/get") => await api.Call<ApartmentFunctions>((f, r, c) => f.Get(r, c), token: token),
                ("GET", "appartment/receipts") => await api.Call<ReceiptFunctions>((f, r, c) => f.All(r, c), token: token),
                ("GET", "collection/summary") => await api.Call<CollectionFunctions>((f, r, c) => f.Summary(r, c), token: token, query: query),
                ("POST", "collection/remind") => await api.Call<CollectionFunctions>((f, r, c) => f.Remind(r, c), token: token, rawBody: body),
                ("POST", "receipt/appartment/save-status") => await api.Call<ReceiptFunctions>((f, r, c) => f.SaveStatus(r, c), token: token, rawBody: body),
                ("GET", "appartment/pending/get") => await api.Call<ApartmentFunctions>((f, r, c) => f.Pending(r, c), token: token),
                ("GET", "appartment/approved/get") => await api.Call<ApartmentFunctions>((f, r, c) => f.Approved(r, c), token: token),
                ("POST", "appartment/approve") => await api.Call<ApartmentFunctions>((f, r, c) => f.Approve(r, c), token: token, rawBody: body),
                ("POST", "appartment/reject") => await api.Call<ApartmentFunctions>((f, r, c) => f.Reject(r, c), token: token, rawBody: body),
                ("GET", "get-receipt-format") => await api.Call<ReceiptFunctions>((f, r, c) => f.GetFormat(r, c), token: token),
                ("POST", "receipt/add") => await api.Call<ReceiptFunctions>((f, r, c) => f.Add(r, c), token: token, rawBody: body),
                _ => null,
            };
            if (reply is null) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"No route {path}") };
            return new HttpResponseMessage((HttpStatusCode)reply.Status) { Content = new StringContent(reply.Body, Encoding.UTF8) };
        }
    }
}
