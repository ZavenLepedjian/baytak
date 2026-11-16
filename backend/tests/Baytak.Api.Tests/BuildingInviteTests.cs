using Baytak.Api.Data;
using Baytak.Api.Domain;
using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;

namespace Baytak.Api.Tests;

/// <summary>The building's invite code: made by the administrator, shared over WhatsApp, used by residents to find the building.</summary>
public class BuildingInviteTests
{
    private const string InvalidMessage = "This invite code is not valid or has expired";

    private static async Task<(string Admin, string Code)> AdminWithCodeAsync(TestApp app)
    {
        var admin = await app.LoginAsApprovedAdminAsync("03 111 111");
        Assert.True((await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), SeedHelpers.BuildingBody, admin)).Ok);
        var made = await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Create(r, ct), body: new { }, token: admin);
        Assert.True(made.Ok, made.Body);
        return (admin, made.Json.GetProperty("code").GetString()!);
    }

    private static Task<Response> LookupAsync(TestApp app, string token, string code) =>
        app.Call<BuildingInviteFunctions>((f, r, ct) => f.Lookup(r, ct), token: token, query: $"?code={Uri.EscapeDataString(code)}");

    private static Task<Response> JoinAsync(TestApp app, string token, object body) =>
        app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), body, token);

    [Fact]
    public async Task The_administrator_makes_a_code_that_works_for_eight_hours()
    {
        using var app = new TestApp();
        var (admin, code) = await AdminWithCodeAsync(app);

        Assert.Equal(InviteCodes.Length, code.Length);
        Assert.Equal(code, InviteCodes.Normalize(code));

        var current = await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Get(r, ct), token: admin);
        Assert.True(current.Json.GetProperty("active").GetBoolean());
        Assert.Equal(code, current.Json.GetProperty("code").GetString());
        Assert.Equal("Tower", current.Json.GetProperty("buildingName").GetString());
        Assert.Equal(app.Time.GetUtcNow().AddHours(8), current.Json.GetProperty("expiresAt").GetDateTimeOffset());

        var resident = await app.LoginAsync("70 222 222");
        app.Time.Advance(TimeSpan.FromHours(7) + TimeSpan.FromMinutes(59));
        Assert.True((await LookupAsync(app, resident, code)).Ok);

        app.Time.Advance(TimeSpan.FromMinutes(2));
        var late = await LookupAsync(app, resident, code);
        Assert.Equal(404, late.Status);
        Assert.Equal(InvalidMessage, late.Body);

        var after = await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Get(r, ct), token: admin);
        Assert.False(after.Json.GetProperty("active").GetBoolean());
        Assert.Equal(JsonValueKindNull, after.Json.GetProperty("code").ValueKind);
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    [Fact]
    public async Task A_new_code_replaces_the_old_one_and_stopping_it_ends_it()
    {
        using var app = new TestApp();
        var (admin, first) = await AdminWithCodeAsync(app);
        var resident = await app.LoginAsync("70 222 222");

        var again = await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Create(r, ct), body: new { }, token: admin);
        var second = again.Json.GetProperty("code").GetString()!;
        Assert.NotEqual(first, second);
        Assert.Equal(404, (await LookupAsync(app, resident, first)).Status);
        Assert.True((await LookupAsync(app, resident, second)).Ok);
        Assert.Equal(1, await app.Db(db => db.BuildingInvites.CountAsync()));

        var stopped = await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Revoke(r, ct), body: new { }, token: admin);
        Assert.Equal("The invite code was stopped", stopped.Message);
        Assert.Equal(404, (await LookupAsync(app, resident, second)).Status);

        // Stopping a code that is already gone is not an error.
        Assert.True((await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Revoke(r, ct), body: new { }, token: admin)).Ok);
    }

    [Fact]
    public async Task Only_the_administrator_manages_the_code_and_it_needs_a_building()
    {
        using var app = new TestApp();
        var (_, code) = await AdminWithCodeAsync(app);
        var resident = await app.LoginAsync("70 222 222");

        Assert.Equal(403, (await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Get(r, ct), token: resident)).Status);
        Assert.Equal(403, (await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Create(r, ct), body: new { }, token: resident)).Status);
        Assert.Equal(403, (await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Revoke(r, ct), body: new { }, token: resident)).Status);
        Assert.Equal(401, (await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Lookup(r, ct), query: $"?code={code}")).Status);

        var noBuilding = await app.LoginAsApprovedAdminAsync("03 999 999");
        var reply = await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Create(r, ct), body: new { }, token: noBuilding);
        Assert.Equal(400, reply.Status);
        Assert.Equal("Please set up your building first", reply.Body);
    }

    [Fact]
    public async Task A_code_names_the_building_for_any_signed_in_person()
    {
        using var app = new TestApp();
        var (_, code) = await AdminWithCodeAsync(app);
        var resident = await app.LoginAsync("70 222 222");

        // As typed or copied: lower case, with a dash or spaces.
        var typed = $"{code[..4].ToLowerInvariant()}-{code[4..]}";
        var found = await LookupAsync(app, resident, typed);

        Assert.True(found.Ok, found.Body);
        Assert.Equal("Tower", found.Json.GetProperty("buildingName").GetString());
        Assert.Equal("Main", found.Json.GetProperty("street").GetString());
        Assert.Equal(6, found.Json.GetProperty("numberOfFloors").GetInt32());

        foreach (var bad in new[] { "", "ABC", "AAAAAAAA", "O0O0O0O0", "ABCD2345XYZ" })
            Assert.Equal(404, (await LookupAsync(app, resident, bad)).Status);
    }

    [Fact]
    public async Task Residents_join_with_the_code_and_the_administrator_still_approves()
    {
        using var app = new TestApp();
        var (admin, code) = await AdminWithCodeAsync(app);
        await app.Db(async db =>
        {
            (await db.Users.SingleAsync()).ExpoPushToken = "ExponentPushToken[admin-token-abcdefgh]";
            await db.SaveChangesAsync();
        });

        // Two neighbours from the same message: the code is not used up, and needs no building id.
        foreach (var (phone, number, floor) in new[] { ("70 222 222", "2A", 2), ("71 333 333", "3B", 3) })
        {
            var token = await app.LoginAsync(phone);
            var sent = await JoinAsync(app, token, new { appartmentNumber = number, appartmentDescription = "d", floor, inviteCode = code });
            Assert.True(sent.Ok, sent.Body);
        }

        var pending = (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json.EnumerateArray().ToList();
        Assert.Equal(["2A", "3B"], pending.Select(a => a.GetProperty("appartmentNumber").GetString()).Order());
        Assert.Equal(0, await app.Db(db => db.Apartments.CountAsync(a => a.Status == ApartmentStatuses.Approved)));
        Assert.Equal(2, app.Push.Sent.Count(p => p.Title == "New join request"));
    }

    [Fact]
    public async Task The_code_decides_the_building_even_if_another_one_is_named()
    {
        using var app = new TestApp();
        var (_, code) = await AdminWithCodeAsync(app);

        var otherAdmin = await app.LoginAsApprovedAdminAsync("03 777 777");
        Assert.True((await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), new
        {
            buildingNumber = "9", buildingName = "Other", street = "Side", city = "Beirut", country = "LB", numberOfFloors = 6, latitude = 33.8, longitude = 35.4,
        }, otherAdmin)).Ok);
        var otherId = await app.Db(db => db.Buildings.Where(b => b.BuildingName == "Other").Select(b => b.Id).SingleAsync());
        var towerId = await app.Db(db => db.Buildings.Where(b => b.BuildingName == "Tower").Select(b => b.Id).SingleAsync());

        var resident = await app.LoginAsync("70 222 222");
        var sent = await JoinAsync(app, resident, new { appartmentNumber = "2A", appartmentDescription = "d", floor = 2, buildingId = otherId, inviteCode = code });

        Assert.True(sent.Ok, sent.Body);
        Assert.Equal(towerId, await app.Db(db => db.Apartments.Select(a => a.BuildingId).SingleAsync()));
    }

    [Fact]
    public async Task A_wrong_stopped_or_expired_code_cannot_be_used_to_join()
    {
        using var app = new TestApp();
        var (admin, code) = await AdminWithCodeAsync(app);
        var resident = await app.LoginAsync("70 222 222");
        var body = (string c) => new { appartmentNumber = "2A", appartmentDescription = "d", floor = 2, inviteCode = c };

        var wrong = await JoinAsync(app, resident, body("ZZZZ2222"));
        Assert.Equal(400, wrong.Status);
        Assert.Equal(InvalidMessage, wrong.Body);

        await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Revoke(r, ct), body: new { }, token: admin);
        Assert.Equal(InvalidMessage, (await JoinAsync(app, resident, body(code))).Body);

        var fresh = (await app.Call<BuildingInviteFunctions>((f, r, ct) => f.Create(r, ct), body: new { }, token: admin)).Json.GetProperty("code").GetString()!;
        app.Time.Advance(TimeSpan.FromHours(9));
        Assert.Equal(InvalidMessage, (await JoinAsync(app, resident, body(fresh))).Body);

        Assert.Equal(0, await app.Db(db => db.Apartments.CountAsync()));
    }

    [Fact]
    public async Task Joining_without_a_code_works_as_before()
    {
        using var app = new TestApp();
        await AdminWithCodeAsync(app);
        var resident = await app.LoginAsync("70 222 222");
        var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: resident)).Json.EnumerateArray().Last().GetProperty("value").GetInt32();

        var sent = await JoinAsync(app, resident, new { appartmentNumber = "2A", appartmentDescription = "d", floor = 2, buildingId });
        Assert.True(sent.Ok, sent.Body);

        var missing = await JoinAsync(app, await app.LoginAsync("71 333 333"), new { appartmentNumber = "3B", appartmentDescription = "d", floor = 3 });
        Assert.Equal("Please select the building you want to join", missing.Body);
    }

    [Fact]
    public async Task Deleting_the_administrators_account_takes_the_code_with_it()
    {
        using var app = new TestApp();
        var (admin, _) = await AdminWithCodeAsync(app);

        var deleted = await app.Call<UserFunctions>((f, r, ct) => f.DeleteAccount(r, ct), body: new { }, token: admin);

        Assert.True(deleted.Ok, deleted.Body);
        Assert.Equal(0, await app.Db(db => db.BuildingInvites.CountAsync()));
    }

    [Fact]
    public void Codes_use_an_alphabet_without_look_alike_characters()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => InviteCodes.New()).ToList();

        Assert.All(codes, c => Assert.Equal(InviteCodes.Length, c.Length));
        Assert.All(codes, c => Assert.DoesNotContain(c, ch => "01OIL".Contains(ch)));
        Assert.All(codes, c => Assert.Equal(c, InviteCodes.Normalize(c)));
        Assert.True(codes.Distinct().Count() > 495);

        Assert.Equal("ABCD2345", InviteCodes.Normalize(" abcd-2345 "));
        Assert.Null(InviteCodes.Normalize("ABCD234"));
        Assert.Null(InviteCodes.Normalize("ABCD2340"));
        Assert.Null(InviteCodes.Normalize(null));
    }
}
