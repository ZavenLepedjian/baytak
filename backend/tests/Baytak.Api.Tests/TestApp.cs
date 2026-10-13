using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Baytak.Api.Auth;
using Baytak.Api.Auth.Sms;
using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Functions;
using Baytak.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Baytak.Api.Tests;

public sealed class FakeSms : ISmsSender
{
    public List<(string To, string Body)> Sent { get; } = new();
    public bool Fail { get; set; }

    public Task SendAsync(string toE164, string body, CancellationToken ct)
    {
        if (Fail) throw new HttpRequestException("provider down");
        Sent.Add((toE164, body));
        return Task.CompletedTask;
    }

    public string LastCodeFor(string phone)
    {
        var message = Sent.Last(m => m.To == phone).Body;
        return Regex.Match(message, @"\b\d{6}\b").Value;
    }
}

public sealed class FakePush : IPushSender
{
    public List<(string?[] Tokens, string Title, string Body)> Sent { get; } = new();

    public Task SendAsync(IEnumerable<string?> tokens, string title, string body, CancellationToken ct)
    {
        Sent.Add((tokens.ToArray(), title, body));
        return Task.CompletedTask;
    }
}

public sealed record Response(int Status, string Body)
{
    public bool Ok => Status is >= 200 and < 300;
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
    /// <summary>The JSON string payload (the API's "message" responses).</summary>
    public string Message => Json.GetString()!;
}

/// <summary>
/// Hosts the real function classes against an in-memory SQLite database, without the Functions host.
/// Requests are plain <see cref="HttpRequest"/> objects, so handlers run exactly as they do in Azure.
/// </summary>
public sealed class TestApp : IDisposable
{
    private readonly SqliteConnection _connection;
    public ServiceProvider Services { get; }
    public FakeSms Sms { get; } = new();
    public FakePush Push { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2025, 3, 1, 9, 0, 0, TimeSpan.Zero));

    public TestApp(Action<OtpOptions>? configureOtp = null, Action<SmsOptions>? configureSms = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<JwtOptions>(o => o.Key = new string('k', 48));
        services.Configure<OtpOptions>(o => configureOtp?.Invoke(o));
        services.Configure<SmsOptions>(o => configureSms?.Invoke(o));
        services.Configure<PhoneOptions>(_ => { });
        services.AddSingleton<TimeProvider>(Time);
        services.AddDbContext<BaytakDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAccessGuard, AccessGuard>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddSingleton<ISmsSender>(Sms);
        services.AddSingleton<IPushSender>(Push);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BaytakDbContext>().Database.EnsureCreated();
    }

    /// <summary>Runs one request against a function class, in its own DI scope (like one invocation).</summary>
    public async Task<Response> Call<T>(
        Func<T, HttpRequest, CancellationToken, Task<IActionResult>> handler,
        object? body = null, string? token = null, string? query = null, string? rawBody = null) where T : class
    {
        using var scope = Services.CreateScope();
        var function = ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider);

        var context = new DefaultHttpContext();
        var request = context.Request;
        request.Method = body is null && rawBody is null ? "GET" : "POST";
        if (query is not null) request.QueryString = new QueryString(query);
        if (token is not null) request.Headers.Authorization = "Bearer " + token;
        if (body is not null || rawBody is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(rawBody ?? JsonSerializer.Serialize(body));
            request.Body = new MemoryStream(bytes);
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length;
        }

        var result = Assert.IsType<ContentResult>(await handler(function, request, CancellationToken.None));
        return new Response(result.StatusCode ?? 200, result.Content ?? "");
    }

    public async Task<T> Db<T>(Func<BaytakDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<BaytakDbContext>());
    }

    public Task Db(Func<BaytakDbContext, Task> action) => Db<int>(async db => { await action(db); return 0; });

    /// <summary>Full login: request a code, read it from the fake SMS, verify it. Returns the JWT.</summary>
    public async Task<string> LoginAsync(string phone, bool requestAdmin = false)
    {
        var login = await Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = phone, status = requestAdmin ? "request-admin" : "" });
        Assert.True(login.Ok, login.Body);

        var normalized = PhoneNumberFor(phone);
        var validate = await Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = phone, pincode = Sms.LastCodeFor(normalized) });
        Assert.True(validate.Ok, validate.Body);
        Time.Advance(TimeSpan.FromSeconds(31)); // clear the resend cool-down for the next login in the same test
        return validate.Json.GetProperty("auth").GetString()!;
    }

    /// <summary>Plays the role of the operator who approves administrator requests.</summary>
    public Task ApproveAdminAsync(string phone) => Db(async db =>
    {
        var normalized = PhoneNumberFor(phone);
        var user = await db.Users.SingleAsync(u => u.PhoneNumber == normalized);
        user.Status = UserStatuses.Approved;
        await db.SaveChangesAsync();
    });

    public async Task<string> LoginAsApprovedAdminAsync(string phone)
    {
        await LoginAsync(phone, requestAdmin: true);
        await ApproveAdminAsync(phone);
        return await LoginAsync(phone); // a fresh token, as the app would after approval
    }

    public static string PhoneNumberFor(string phone) => Domain.PhoneNumber.Normalize(phone, "+961")!;

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}
