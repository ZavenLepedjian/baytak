using Baytak.Api.Auth;
using Baytak.Api.Data;
using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Baytak.Api.Tests;

public class BuildingAdminTests
{
    private const string AdminPhone = "03 111 111";
    private const string PushToken = "ExponentPushToken[abcdefghijklmnop]";

    private static Task<Response> TransferAsync(TestApp app, string token, object body) =>
        app.Call<BuildingAdminFunctions>((f, r, ct) => f.Transfer(r, ct), body, token);

    private static Task<Response> RefreshAsync(TestApp app, string token) =>
        app.Call<AuthFunctions>((f, r, ct) => f.Refresh(r, ct), body: new { }, token: token);

    private static Task<Response> OverviewAsync(TestApp app, string token) =>
        app.Call<OverviewFunctions>((f, r, ct) => f.Overview(r, ct), token: token);

    private static string RoleOf(TestApp app, string token) => app.Services.GetRequiredService<ITokenService>().Inspect(token)!.Value.Role;

    [Fact]
    public async Task The_administrator_hands_the_building_to_a_resident_and_both_pick_up_the_change()
    {
        using var app = new TestApp();
        var (admin, ids, residents) = await SeedHelpers.BuildingWithResidentsAsync(app);
        await app.Db(async db =>
        {
            (await db.Users.SingleAsync(u => u.FirstName == "Res1A")).ExpoPushToken = PushToken;
            await db.SaveChangesAsync();
        });

        var transferred = await TransferAsync(app, admin, new { apartmentId = ids[0] });
        Assert.True(transferred.Ok, transferred.Body);

        var building = await app.Db(db => db.Buildings.Include(b => b.Admin).SingleAsync());
        Assert.Equal("Res1A", building.Admin!.FirstName);
        Assert.Equal(Roles.Admin, building.Admin.Role);
        Assert.Equal(UserStatuses.Approved, building.Admin.Status);
        Assert.Equal(Roles.User, (await app.Db(db => db.Users.SingleAsync(u => u.PhoneNumber == TestApp.PhoneNumberFor(AdminPhone)))).Role);

        // The person who was told gets a notification.
        var push = Assert.Single(app.Push.Sent, p => p.Title == "You now manage the building");
        Assert.Equal(new[] { PushToken }, push.Tokens);
        Assert.Contains("Tower", push.Body);

        // The server follows the database, not the token: the former administrator is turned away at once and the new one
        // works even before their app has a new token.
        Assert.Equal(403, (await OverviewAsync(app, admin)).Status);
        var asNew = await OverviewAsync(app, residents[0]);
        Assert.True(asNew.Ok, asNew.Body);
        Assert.True(asNew.Json.GetProperty("hasBuilding").GetBoolean());
        Assert.Equal("Tower", asNew.Json.GetProperty("buildingName").GetString());

        // Each app asks for a token that says the new role, with the expiry the old token had.
        var oldExpiry = app.Services.GetRequiredService<ITokenService>().Inspect(residents[0])!.Value.Expires;
        var newAdminToken = (await RefreshAsync(app, residents[0])).Json.GetProperty("auth").GetString()!;
        Assert.Equal(Roles.Admin, RoleOf(app, newAdminToken));
        Assert.Equal(oldExpiry, app.Services.GetRequiredService<ITokenService>().Inspect(newAdminToken)!.Value.Expires);
        var formerToken = (await RefreshAsync(app, admin)).Json.GetProperty("auth").GetString()!;
        Assert.Equal(Roles.User, RoleOf(app, formerToken));

        // The new administrator runs the building: the old one cannot hand it back.
        Assert.Equal(403, (await TransferAsync(app, admin, new { apartmentId = ids[1] })).Status);
        Assert.True((await TransferAsync(app, newAdminToken, new { apartmentId = ids[1] })).Ok);
        Assert.Equal("Res2A", (await app.Db(db => db.Buildings.Include(b => b.Admin).SingleAsync())).Admin!.FirstName);
    }

    [Fact]
    public async Task A_token_is_only_replaced_when_the_role_changed()
    {
        using var app = new TestApp();
        var (admin, _, residents) = await SeedHelpers.BuildingWithResidentsAsync(app);

        // Nothing changed: the same token comes back, so refreshing cannot extend a session.
        Assert.Equal(admin, (await RefreshAsync(app, admin)).Json.GetProperty("auth").GetString());
        Assert.Equal(residents[0], (await RefreshAsync(app, residents[0])).Json.GetProperty("auth").GetString());
        Assert.Equal(401, (await RefreshAsync(app, "nope")).Status);
    }

    [Fact]
    public async Task Only_a_resident_of_this_building_who_may_be_administrator_can_take_over()
    {
        using var app = new TestApp();
        var (admin, ids, residents) = await SeedHelpers.BuildingWithResidentsAsync(app);
        var (otherAdmin, otherIds, _) = await SeedHelpers.BuildingWithResidentsAsync(app, "03 222 222", "71 000 00");

        Assert.Equal(403, (await TransferAsync(app, residents[1], new { apartmentId = ids[0] })).Status);
        Assert.Equal(401, (await TransferAsync(app, "nope", new { apartmentId = ids[0] })).Status);
        Assert.Equal(400, (await TransferAsync(app, admin, new { })).Status);
        Assert.Equal(404, (await TransferAsync(app, admin, new { apartmentId = 99999 })).Status);
        Assert.Equal(404, (await TransferAsync(app, admin, new { apartmentId = otherIds[0] })).Status); // another building's resident

        // A join request still waiting is not a member yet.
        var waiting = await app.LoginAsync("70 888 888");
        var buildingId = await app.Db(db => db.Buildings.Where(b => b.Admin!.PhoneNumber == TestApp.PhoneNumberFor(AdminPhone)).Select(b => b.Id).SingleAsync());
        Assert.True((await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = "9Z", appartmentDescription = "d", floor = 3, buildingId }, waiting)).Ok);
        var waitingApartment = await app.Db(db => db.Apartments.SingleAsync(a => a.ApartmentNumber == "9Z"));
        Assert.Equal(404, (await TransferAsync(app, admin, new { apartmentId = waitingApartment.Id })).Status);

        // Not an administrator whom the operator turned down, and not one who already runs another building.
        await app.Db(async db =>
        {
            var rejected = await db.Users.SingleAsync(u => u.PhoneNumber == TestApp.PhoneNumberFor("70 000 002"));
            rejected.Role = Roles.Admin;
            rejected.Status = UserStatuses.Rejected;
            await db.SaveChangesAsync();
        });
        Assert.Equal(400, (await TransferAsync(app, admin, new { apartmentId = ids[1] })).Status);

        // The administrator of the other building lives here too.
        var otherAdminUser = await app.Db(db => db.Users.SingleAsync(u => u.PhoneNumber == TestApp.PhoneNumberFor("03 222 222")));
        await app.Db(async db =>
        {
            var apartment = await db.Apartments.SingleAsync(a => a.Id == ids[2]);
            db.Apartments.Remove(apartment);
            await db.SaveChangesAsync();
            db.Apartments.Add(new Apartment { UserId = otherAdminUser.Id, BuildingId = apartment.BuildingId, ApartmentNumber = "2B", Description = "d", Floor = 2, Status = ApartmentStatuses.Approved });
            await db.SaveChangesAsync();
        });
        var refused = await TransferAsync(app, admin, new { apartmentId = (await app.Db(db => db.Apartments.SingleAsync(a => a.UserId == otherAdminUser.Id))).Id });
        Assert.Equal(400, refused.Status);
        Assert.Contains("another building", refused.Body);

        // Nothing moved.
        Assert.Equal(TestApp.PhoneNumberFor(AdminPhone), (await app.Db(db => db.Buildings.Include(b => b.Admin).OrderBy(b => b.Id).FirstAsync())).Admin!.PhoneNumber);
        Assert.DoesNotContain(app.Push.Sent, p => p.Title == "You now manage the building");
        _ = otherAdmin;
    }

    [Fact]
    public async Task An_administrator_cannot_hand_the_building_to_themself_or_without_a_building()
    {
        using var app = new TestApp();
        var lonely = await app.LoginAsApprovedAdminAsync(AdminPhone);
        var none = await TransferAsync(app, lonely, new { apartmentId = 1 });
        Assert.Equal(400, none.Status);
        Assert.Contains("set up your building", none.Body);

        var (admin, _, _) = await SeedHelpers.BuildingWithResidentsAsync(app, "03 333 333", "72 000 00");
        // The administrator also lives in the building.
        await app.Db(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.PhoneNumber == TestApp.PhoneNumberFor("03 333 333"));
            var building = await db.Buildings.SingleAsync(b => b.AdminUserId == user.Id);
            db.Apartments.Add(new Apartment { UserId = user.Id, BuildingId = building.Id, ApartmentNumber = "PH", Description = "d", Floor = 6, Status = ApartmentStatuses.Approved });
            await db.SaveChangesAsync();
        });
        var own = await app.Db(db => db.Apartments.SingleAsync(a => a.ApartmentNumber == "PH"));
        var self = await TransferAsync(app, admin, new { apartmentId = own.Id });
        Assert.Equal(400, self.Status);
        Assert.Contains("already manage", self.Body);
    }

    [Fact]
    public async Task What_the_building_holds_stays_and_the_former_administrator_can_leave_cleanly()
    {
        using var app = new TestApp();
        var (admin, ids, residents) = await SeedHelpers.BuildingWithResidentsAsync(app);
        var receipt = new
        {
            currency = "USD", appartmentIds = "",
            receipt = new { currency = "USD", date = "2025-02-20", customFields = new[] { new { fieldName = "Maintenance", type = "Amount", value = "40" } } },
        };
        Assert.True((await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), receipt, admin)).Ok);

        Assert.True((await TransferAsync(app, admin, new { apartmentId = ids[0] })).Ok);

        // The receipt is still the building's, now under the new administrator.
        var newToken = (await RefreshAsync(app, residents[0])).Json.GetProperty("auth").GetString()!;
        Assert.Equal(1, (await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: newToken)).Json.GetProperty("receipts").GetArrayLength());
        var summary = await app.Call<SummaryFunctions>((f, r, ct) => f.Summary(r, ct), token: newToken, query: "?month=2025-02");
        Assert.True(summary.Json.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(1, summary.Json.GetProperty("receiptsIssued").GetInt32());

        // The former administrator deletes their account: what they issued moves to the current administrator instead of blocking it.
        var formerToken = (await RefreshAsync(app, admin)).Json.GetProperty("auth").GetString()!;
        var deleted = await app.Call<UserFunctions>((f, r, ct) => f.DeleteAccount(r, ct), body: new { }, token: formerToken);
        Assert.True(deleted.Ok, deleted.Body);
        var building = await app.Db(db => db.Buildings.Include(b => b.Admin).SingleAsync());
        Assert.Equal("Res1A", building.Admin!.FirstName);
        Assert.Equal(1, await app.Db(db => db.Receipts.CountAsync(r => r.CreatedByUserId == building.AdminUserId)));
    }
}
