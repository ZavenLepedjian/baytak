using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;

namespace Baytak.Api.Tests;

/// <summary>auth/service-login: how the WhatsApp assistant signs in the person who wrote, without an SMS code.</summary>
public class ServiceLoginTests
{
    private static (string Name, string Value)[] Key(string key = TestApp.ServiceKey) => [(AuthFunctions.ServiceKeyHeader, key)];

    [Fact]
    public async Task Signs_in_an_existing_account_without_a_code_for_a_short_time()
    {
        using var app = new TestApp();
        var appToken = await app.LoginAsync("70 123 456");
        var sms = app.Sms.Sent.Count;

        // WhatsApp gives the number with its country code; the app user typed it the local way.
        var reply = await app.Call<AuthFunctions>((f, r, ct) => f.ServiceLogin(r, ct), new { phoneNumber = "+96170123456" }, headers: Key());

        Assert.True(reply.Ok, reply.Body);
        Assert.Equal("User", reply.Json.GetProperty("role").GetString());
        Assert.Equal("Approved", reply.Json.GetProperty("userStatus").GetString());
        Assert.Equal(sms, app.Sms.Sent.Count);

        var token = reply.Json.GetProperty("auth").GetString()!;
        Assert.True((await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: token)).Ok);

        app.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: token)).Status);
        Assert.True((await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: appToken)).Ok); // the phone app stays signed in
    }

    [Fact]
    public async Task Tells_an_approved_administrator_apart()
    {
        using var app = new TestApp();
        await app.LoginAsApprovedAdminAsync("03 111 111");

        var reply = await app.Call<AuthFunctions>((f, r, ct) => f.ServiceLogin(r, ct), new { phoneNumber = "+9613111111" }, headers: Key());

        Assert.Equal("Admin", reply.Json.GetProperty("role").GetString());
        Assert.Equal("Approved", reply.Json.GetProperty("userStatus").GetString());
    }

    [Fact]
    public async Task Is_refused_without_the_service_key()
    {
        using var app = new TestApp();
        await app.LoginAsync("70 123 456");

        var missing = await app.Call<AuthFunctions>((f, r, ct) => f.ServiceLogin(r, ct), new { phoneNumber = "+96170123456" });
        Assert.Equal(401, missing.Status);

        var wrong = await app.Call<AuthFunctions>((f, r, ct) => f.ServiceLogin(r, ct), new { phoneNumber = "+96170123456" }, headers: Key(TestApp.ServiceKey + "x"));
        Assert.Equal(401, wrong.Status);
        Assert.Equal("Unauthorized", wrong.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-to-be-a-secret")]
    public async Task Is_switched_off_until_a_long_key_is_configured(string? configured)
    {
        using var app = new TestApp(serviceKey: configured);
        await app.LoginAsync("70 123 456");

        // Even presenting exactly what is configured does not open it.
        var reply = await app.Call<AuthFunctions>((f, r, ct) => f.ServiceLogin(r, ct), new { phoneNumber = "+96170123456" }, headers: Key(configured ?? ""));

        Assert.Equal(404, reply.Status);
    }

    [Theory]
    [InlineData("+96176999000")]
    [InlineData("not a number")]
    [InlineData("")]
    public async Task Never_creates_an_account(string phone)
    {
        using var app = new TestApp();
        await app.LoginAsync("70 123 456");
        var users = await app.Db(db => db.Users.CountAsync());

        var reply = await app.Call<AuthFunctions>((f, r, ct) => f.ServiceLogin(r, ct), new { phoneNumber = phone }, headers: Key());

        Assert.Equal(404, reply.Status);
        Assert.Equal("NoAccount", reply.Body);
        Assert.Equal(users, await app.Db(db => db.Users.CountAsync()));
    }
}
