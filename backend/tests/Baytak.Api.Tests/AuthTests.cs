using System.IdentityModel.Tokens.Jwt;
using Baytak.Api.Auth;
using Baytak.Api.Data;
using Baytak.Api.Domain;
using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Baytak.Api.Tests;

public class AuthTests
{
    private const string Phone = "03 123 456";
    private const string Normalized = "+9613123456";

    [Fact]
    public async Task Login_sends_a_code_and_validate_returns_a_token_the_app_can_read()
    {
        using var app = new TestApp();
        var login = await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone, status = "", @long = "35.5", lat = "33.9" });
        Assert.True(login.Ok, login.Body);
        Assert.Equal(Normalized, Assert.Single(app.Sms.Sent).To);

        var validate = await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = app.Sms.LastCodeFor(Normalized) });
        Assert.True(validate.Ok, validate.Body);
        Assert.Equal("Approved", validate.Json.GetProperty("userStatus").GetString());

        // The mobile app decodes these claims client-side.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(validate.Json.GetProperty("auth").GetString());
        Assert.Equal(Normalized, jwt.Claims.Single(c => c.Type == "phoneNumber").Value);
        Assert.Equal("User", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.True(jwt.ValidTo > app.Time.GetUtcNow().UtcDateTime.AddDays(29));
    }

    [Fact]
    public async Task Codes_are_stored_hashed_and_single_use()
    {
        using var app = new TestApp();
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone });
        var code = app.Sms.LastCodeFor(Normalized);

        var stored = await app.Db(db => db.OtpCodes.SingleAsync());
        Assert.DoesNotContain(code, stored.CodeHash);

        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = code })).Ok);
        Assert.Equal(400, (await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = code })).Status);
    }

    [Fact]
    public async Task Wrong_codes_are_rejected_and_lock_after_too_many_attempts()
    {
        using var app = new TestApp(o => o.MaxAttempts = 3);
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone });
        var real = app.Sms.LastCodeFor(Normalized);
        var wrong = real == "000000" ? "111111" : "000000";

        for (var i = 0; i < 3; i++)
            Assert.Equal(400, (await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = wrong })).Status);

        // Even the right code no longer works: a new one must be requested.
        Assert.Equal(429, (await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = real })).Status);
    }

    [Fact]
    public async Task Codes_expire()
    {
        using var app = new TestApp();
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone });
        var code = app.Sms.LastCodeFor(Normalized);
        app.Time.Advance(TimeSpan.FromMinutes(6));
        var result = await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = code });
        Assert.Equal(400, result.Status);
        Assert.Contains("expired", result.Body);
    }

    [Fact]
    public async Task Resending_is_rate_limited_and_invalidates_the_previous_code()
    {
        using var app = new TestApp();
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone });
        var first = app.Sms.LastCodeFor(Normalized);

        Assert.Equal(429, (await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone })).Status); // too soon
        app.Time.Advance(TimeSpan.FromSeconds(31));
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone })).Ok);
        var second = app.Sms.LastCodeFor(Normalized);

        if (first != second)
            Assert.Equal(400, (await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = first })).Status);
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = second })).Ok);
    }

    [Fact]
    public async Task Daily_limit_applies()
    {
        using var app = new TestApp(o => o.MaxPerDay = 3);
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone })).Ok);
            app.Time.Advance(TimeSpan.FromSeconds(31));
        }
        Assert.Equal(429, (await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone })).Status);
        app.Time.Advance(TimeSpan.FromHours(25));
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone })).Ok);
    }

    [Fact]
    public async Task Sms_failure_is_reported_and_leaves_no_usable_code()
    {
        using var app = new TestApp();
        app.Sms.Fail = true;
        Assert.Equal(500, (await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone })).Status);
        Assert.Equal(0, await app.Db(db => db.OtpCodes.CountAsync()));

        // The user can try again immediately (the failed attempt didn't start the cool-down).
        app.Sms.Fail = false;
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone })).Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12")]
    public async Task Invalid_phone_numbers_are_rejected(string phone)
    {
        using var app = new TestApp();
        Assert.Equal(400, (await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = phone })).Status);
        Assert.Empty(app.Sms.Sent);
    }

    [Fact]
    public async Task Malformed_bodies_get_a_clean_400()
    {
        using var app = new TestApp();
        Assert.Equal(400, (await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), rawBody: "{not json")).Status);
        Assert.Equal(400, (await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), rawBody: "null")).Status);
    }

    [Fact]
    public async Task Request_admin_sent_with_the_resend_leaves_the_account_pending()
    {
        using var app = new TestApp();
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone, status = "request-admin" });
        app.Time.Advance(TimeSpan.FromSeconds(31));
        // The app re-sends the status itself when it asks for a new code.
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone, status = "request-admin" });

        var validate = await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = app.Sms.LastCodeFor(Normalized) });
        Assert.Equal("Pending", validate.Json.GetProperty("userStatus").GetString());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(validate.Json.GetProperty("auth").GetString());
        Assert.Equal("Admin", jwt.Claims.Single(c => c.Type == "role").Value);
    }

    [Fact]
    public async Task Pending_admins_cannot_use_admin_endpoints_until_approved()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync(Phone, requestAdmin: true);

        // Regular endpoints work (the app uses them to poll its status), admin ones don't.
        Assert.Equal("Pending", (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: token)).Json.GetProperty("userStatus").GetString());
        Assert.Equal(403, (await app.Call<BuildingFunctions>((f, r, ct) => f.Get(r, ct), token: token)).Status);
        Assert.Equal(403, (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: token)).Status);

        // The same token works as soon as the account is approved: no re-login needed.
        await app.ApproveAdminAsync(Phone);
        Assert.True((await app.Call<BuildingFunctions>((f, r, ct) => f.Get(r, ct), token: token)).Ok);
    }

    [Fact]
    public async Task Residents_cannot_use_admin_endpoints_even_with_a_valid_token()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync(Phone);
        Assert.Equal(403, (await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), new { }, token)).Status);
        Assert.Equal(403, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), new { }, token)).Status);
        Assert.Equal(403, (await app.Call<ReceiptFunctions>((f, r, ct) => f.GetFormat(r, ct), token: token)).Status);
    }

    [Fact]
    public async Task Bad_tokens_are_rejected()
    {
        using var app = new TestApp();
        var good = await app.LoginAsync(Phone);

        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct))).Status);                       // no header
        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: "garbage")).Status);
        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: good[..^3] + "abc")).Status); // tampered signature

        // Expired after the configured lifetime.
        app.Time.Advance(TimeSpan.FromDays(31));
        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: good)).Status);
    }

    [Fact]
    public async Task Tokens_signed_with_another_key_or_for_a_deleted_user_are_rejected()
    {
        using var app = new TestApp();
        var good = await app.LoginAsync(Phone);

        var foreign = new TokenService(
            Microsoft.Extensions.Options.Options.Create(new JwtOptions { Key = new string('x', 48) }), app.Time)
            .Issue(new User { Id = Guid.NewGuid(), PhoneNumber = Normalized, Role = Roles.Admin });
        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: foreign)).Status);

        await app.Db(async db => { db.Users.RemoveRange(db.Users); await db.SaveChangesAsync(); });
        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: good)).Status);
    }

    [Fact]
    public async Task Fixed_test_code_works_only_for_listed_numbers_and_sends_no_sms()
    {
        using var app = new TestApp(o => { o.FixedCode = "424242"; o.FixedCodePhones = Normalized; });
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone })).Ok);
        Assert.Empty(app.Sms.Sent);
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = "424242" })).Ok);

        // Any other number gets a real random code.
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = "70 000 111" })).Ok);
        Assert.Single(app.Sms.Sent);
        Assert.Equal(400, (await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = "70 000 111", pincode = "424242" })).Status);
    }

    [Theory]
    [InlineData("03 123 456", "+961", "+9613123456")]
    [InlineData("+961 3 123 456", "+961", "+9613123456")]
    [InlineData("00961 3 123 456", "+961", "+9613123456")]
    [InlineData("71123456", "+961", "+96171123456")]
    [InlineData("(03) 123-456", null, "03123456")]
    [InlineData("+1 (415) 555-0100", "+961", "+14155550100")]
    public void Phone_numbers_are_normalised(string input, string? country, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(input, country));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("+1234567890123456789")]
    public void Implausible_phone_numbers_are_refused(string? input) => Assert.Null(PhoneNumber.Normalize(input, "+961"));

    [Fact]
    public void Push_token_is_bound_to_one_account_and_validated()
    {
        Assert.True(Services.ExpoPushToken.IsValid("ExponentPushToken[abcdefghijklmnop]"));
        Assert.True(Services.ExpoPushToken.IsValid("ExpoPushToken[abcdefghijklmnop]"));
        Assert.False(Services.ExpoPushToken.IsValid("ExponentPushToken[]"));
        Assert.False(Services.ExpoPushToken.IsValid("fcm:abcdef"));
        Assert.False(Services.ExpoPushToken.IsValid(null));
    }

    [Fact]
    public async Task A_device_token_moves_to_the_latest_account_that_registers_it()
    {
        using var app = new TestApp();
        var a = await app.LoginAsync("03 000 001");
        var b = await app.LoginAsync("03 000 002");
        const string token = "ExponentPushToken[shared-device-token]";

        Assert.True((await app.Call<UserFunctions>((f, r, ct) => f.SetupNotification(r, ct), new { notificationToken = token }, a)).Ok);
        Assert.True((await app.Call<UserFunctions>((f, r, ct) => f.SetupNotification(r, ct), new { notificationToken = token }, b)).Ok);
        Assert.Equal(400, (await app.Call<UserFunctions>((f, r, ct) => f.SetupNotification(r, ct), new { notificationToken = "nope" }, b)).Status);

        var owners = await app.Db(db => db.Users.Where(u => u.ExpoPushToken == token).Select(u => u.PhoneNumber).ToListAsync());
        Assert.Equal(new[] { TestApp.PhoneNumberFor("03 000 002") }, owners);
    }
}
