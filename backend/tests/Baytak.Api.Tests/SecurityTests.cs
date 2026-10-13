using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Baytak.Api.Data;
using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Baytak.Api.Tests;

/// <summary>Regression tests for the security review findings and the hardening that followed it.</summary>
public class SecurityTests
{
    private const string Phone = "03 123 456";
    private const string Normalized = "+9613123456";

    private static readonly object BuildingBody = new
    {
        buildingNumber = "1", buildingName = "Tower", street = "Main", city = "Beirut", country = "LB",
        numberOfFloors = 5, latitude = 33.9, longitude = 35.5,
    };

    [Fact]
    public async Task An_attacker_cannot_plant_request_admin_on_someone_elses_login()
    {
        using var app = new TestApp();

        // Anonymous attacker arms an admin request for the victim's number...
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone, status = "request-admin" });
        app.Time.Advance(TimeSpan.FromSeconds(31));

        // ...then the victim signs in normally and even taps "resend", which carries no status.
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone, status = "" });
        app.Time.Advance(TimeSpan.FromSeconds(31));
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone });
        var validate = await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = Phone, pincode = app.Sms.LastCodeFor(Normalized) });

        Assert.Equal("Approved", validate.Json.GetProperty("userStatus").GetString());
        Assert.Equal(Roles.User, (await app.Db(db => db.Users.SingleAsync())).Role);
    }

    [Fact]
    public async Task Sms_is_only_sent_to_allowed_countries_but_listed_test_numbers_still_work()
    {
        using var app = new TestApp(o => { o.FixedCode = "424242"; o.FixedCodePhones = "+14155550100"; });

        var blocked = await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = "+44 7700 900123" });
        Assert.Equal(400, blocked.Status);
        Assert.Empty(app.Sms.Sent);
        Assert.Equal(0, await app.Db(db => db.OtpCodes.CountAsync()));

        // A store-review test number abroad is allowed: it never receives an SMS.
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = "+1 415 555 0100" })).Ok);
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = "+1 415 555 0100", pincode = "424242" })).Ok);
        Assert.Empty(app.Sms.Sent);
    }

    [Fact]
    public async Task The_country_allow_list_can_be_widened()
    {
        using var app = new TestApp(configureSms: o => o.AllowedCountryCodes = "961, +44");
        Assert.True((await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = "+44 7700 900123" })).Ok);
        Assert.Equal(400, (await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = "+33 6 12 34 56 78" })).Status);

        using var open = new TestApp(configureSms: o => o.AllowedCountryCodes = "*");
        Assert.True((await open.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = "+33 6 12 34 56 78" })).Ok);
    }

    [Fact]
    public async Task Logout_revokes_every_issued_token_and_stops_push()
    {
        using var app = new TestApp();
        var first = await app.LoginAsync(Phone);
        var second = await app.LoginAsync(Phone); // another device
        await app.Call<UserFunctions>((f, r, ct) => f.SetupNotification(r, ct), new { notificationToken = "ExponentPushToken[device-token-123]" }, second);

        Assert.True((await app.Call<UserFunctions>((f, r, ct) => f.Logout(r, ct), new { }, second)).Ok);

        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: first)).Status);
        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: second)).Status);
        Assert.Null(await app.Db(db => db.Users.Select(u => u.ExpoPushToken).SingleAsync()));

        var fresh = await app.LoginAsync(Phone);
        Assert.True((await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: fresh)).Ok);
    }

    [Fact]
    public async Task Correctly_signed_tokens_without_a_session_version_are_rejected()
    {
        using var app = new TestApp();
        await app.LoginAsync(Phone);
        var id = await app.Db(db => db.Users.Select(u => u.Id).SingleAsync());

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 48)));
        var now = app.Time.GetUtcNow().UtcDateTime;
        var legacy = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "baytak", "baytak-app",
            [new Claim("sub", id.ToString()), new Claim("role", "Admin")],
            now, now.AddDays(1),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: legacy)).Status);
    }

    [Fact]
    public async Task Residents_can_delete_their_account_and_all_personal_data()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync("03 111 111");
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);

        var resident = await app.LoginAsync(Phone);
        var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: resident)).Json[0].GetProperty("value").GetInt32();
        await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = "1A", appartmentDescription = "d", floor = 1, buildingId }, resident);
        var aptId = (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json[0].GetProperty("id").GetInt32();
        await app.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id = aptId }, admin);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), new
        {
            currency = "USD", appartmentIds = "",
            receipt = new { currency = "USD", date = "2025-03-01", customFields = new[] { new { fieldName = "M", type = "Amount", value = "5" } } },
        }, admin);
        await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), new { description = "x", amount = 1, type = "income" }, resident);

        var deleted = await app.Call<UserFunctions>((f, r, ct) => f.DeleteAccount(r, ct), new { }, resident);
        Assert.True(deleted.Ok, deleted.Body);

        Assert.Equal(401, (await app.Call<UserFunctions>((f, r, ct) => f.Status(r, ct), token: resident)).Status);
        Assert.False(await app.Db(db => db.Users.AnyAsync(u => u.PhoneNumber == Normalized)));
        Assert.Equal(0, await app.Db(db => db.WalletEntries.CountAsync()));
        Assert.Equal(0, await app.Db(db => db.Apartments.CountAsync()));
        Assert.Equal(0, await app.Db(db => db.ReceiptAssignments.CountAsync()));
        Assert.Equal(0, await app.Db(db => db.OtpCodes.CountAsync(o => o.PhoneNumber == Normalized)));

        // The building and the admin are untouched.
        Assert.True((await app.Call<BuildingFunctions>((f, r, ct) => f.Get(r, ct), token: admin)).Ok);
    }

    [Fact]
    public async Task An_admin_with_residents_must_hand_over_before_deleting_but_an_empty_building_is_removed()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync("03 111 111");
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);
        var resident = await app.LoginAsync(Phone);
        var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: resident)).Json[0].GetProperty("value").GetInt32();
        await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = "1A", appartmentDescription = "d", floor = 1, buildingId }, resident);

        Assert.Equal(409, (await app.Call<UserFunctions>((f, r, ct) => f.DeleteAccount(r, ct), new { }, admin)).Status);

        // Once the resident is gone the admin can delete too, and the empty building goes with them.
        Assert.True((await app.Call<UserFunctions>((f, r, ct) => f.DeleteAccount(r, ct), new { }, resident)).Ok);
        Assert.True((await app.Call<UserFunctions>((f, r, ct) => f.DeleteAccount(r, ct), new { }, admin)).Ok);
        Assert.Equal(0, await app.Db(db => db.Buildings.CountAsync()));
        Assert.Equal(0, await app.Db(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Old_login_codes_are_purged()
    {
        using var app = new TestApp();
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = Phone });
        app.Time.Advance(TimeSpan.FromDays(3));
        await app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = "70 000 111" });
        Assert.Equal(1, await app.Db(db => db.OtpCodes.CountAsync()));
    }
}
