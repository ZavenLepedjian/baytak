using Baytak.Api.Data;
using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;

namespace Baytak.Api.Tests;

/// <summary>Walks through what the mobile app does, end to end, against the real handlers.</summary>
public class JourneyTests
{
    private const string Admin = "03 111 111";
    private const string Resident1 = "70 222 222";
    private const string Resident2 = "71 333 333";

    private static readonly object BuildingBody = new
    {
        buildingNumber = "12", buildingName = "Sunrise Tower", street = "Hamra St", city = "Beirut", country = "Lebanon",
        numberOfFloors = 8, latitude = 33.8938, longitude = 35.5018, linkedAppartments = 0,
    };

    private static readonly object FormatBody = new
    {
        currency = "USD",
        customFields = new object[]
        {
            new { fieldName = "Maintenance", type = "Amount", value = "" },
            new { fieldName = "Electricity", type = "Amount", value = "" },
            new { fieldName = "Note", type = "Text", value = "" },
        },
        Date = "",
    };

    [Fact]
    public async Task Admin_sets_up_building_residents_join_and_receipts_flow_through()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync(Admin);

        // Building starts empty, then is saved.
        var empty = await app.Call<BuildingFunctions>((f, r, ct) => f.Get(r, ct), token: admin);
        Assert.Equal("", empty.Json.GetProperty("buildingName").GetString());
        Assert.True((await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin)).Ok);
        var saved = await app.Call<BuildingFunctions>((f, r, ct) => f.Get(r, ct), token: admin);
        Assert.Equal("Sunrise Tower", saved.Json.GetProperty("buildingName").GetString());
        Assert.Equal(0, saved.Json.GetProperty("linkedAppartments").GetInt32());

        // No receipt format yet -> marker; then save one.
        Assert.Equal("SetupReceipt", (await app.Call<ReceiptFunctions>((f, r, ct) => f.GetFormat(r, ct), token: admin)).Message);
        Assert.True((await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), FormatBody, admin)).Ok);
        var formatText = (await app.Call<ReceiptFunctions>((f, r, ct) => f.GetFormat(r, ct), token: admin)).Message;
        using (var format = System.Text.Json.JsonDocument.Parse(formatText))
        {
            Assert.Equal("USD", format.RootElement.GetProperty("currency").GetString());
            Assert.Equal(3, format.RootElement.GetProperty("customFields").GetArrayLength());
        }

        // A resident discovers the building and asks to join; the admin is notified.
        var r1 = await app.LoginAsync(Resident1);
        await app.Call<UserFunctions>((f, r, ct) => f.SetupNotification(r, ct), new { notificationToken = "ExponentPushToken[resident-one-token]" }, r1);
        var buildings = await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: r1);
        var buildingId = buildings.Json[0].GetProperty("value").GetInt32();
        Assert.Contains("Sunrise Tower", buildings.Json[0].GetProperty("label").GetString());

        // Before joining the dashboard asks the resident to register.
        Assert.Equal("Register Apaprtment", (await app.Call<ReceiptFunctions>((f, r, ct) => f.Latest(r, ct), token: r1)).Message);

        var join = await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct),
            new { appartmentNumber = "3A", appartmentDescription = "Sea view", floor = 3, buildingId }, r1);
        Assert.True(join.Ok, join.Body);
        var mine = await app.Call<ApartmentFunctions>((f, r, ct) => f.Get(r, ct), token: r1);
        Assert.Equal("Pending", mine.Json.GetProperty("status").GetString());
        Assert.Equal("Sunrise Tower", mine.Json.GetProperty("buildingName").GetString());

        // Second resident also joins but is never approved.
        var r2 = await app.LoginAsync(Resident2);
        Assert.True((await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct),
            new { appartmentNumber = "5B", appartmentDescription = "Corner", floor = 5, buildingId }, r2)).Ok);

        // Admin sees both requests, approves the first.
        var pending = await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin);
        Assert.Equal(2, pending.Json.GetArrayLength());
        var firstId = pending.Json.EnumerateArray().Single(a => a.GetProperty("appartmentNumber").GetString() == "3A").GetProperty("id").GetInt32();
        var approve = await app.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id = firstId }, admin);
        Assert.True(approve.Ok, approve.Body);
        Assert.Contains(app.Push.Sent, p => p.Title == "Request approved" && p.Tokens.Contains("ExponentPushToken[resident-one-token]"));

        var approved = await app.Call<ApartmentFunctions>((f, r, ct) => f.Approved(r, ct), token: admin);
        Assert.Single(approved.Json.EnumerateArray());
        Assert.Equal(1, (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json.GetArrayLength());

        // Building is now locked.
        Assert.False((await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin)).Ok);

        // Resident with an approved apartment but no receipts yet.
        var none = await app.Call<ReceiptFunctions>((f, r, ct) => f.Latest(r, ct), token: r1);
        Assert.Equal(1, none.Json.GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, none.Json[0].GetProperty("receipt").ValueKind);
        Assert.Equal("3A", none.Json[0].GetProperty("appartment").GetProperty("appartmentNumber").GetString());

        // Admin issues a receipt. The client-supplied total is ignored; amounts are summed server-side.
        // (older app builds sent both "date" and "Date" and a client-computed total)
        var add = await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), token: admin, rawBody: """
            {"currency":"USD","total":999999,"appartmentIds":"","version":"v2.0",
             "receipt":{"currency":"USD","date":"2025-03-01","Date":"2025-03-01",
               "customFields":[
                 {"fieldName":"Maintenance","type":"Amount","value":"100"},
                 {"fieldName":"Electricity","type":"Amount","value":"50.5"},
                 {"fieldName":"Note","type":"Text","value":"Pay by the 10th"}]}}
            """);
        Assert.True(add.Ok, add.Body);
        Assert.Contains(app.Push.Sent, p => p.Title == "New receipt" && p.Tokens.Contains("ExponentPushToken[resident-one-token]"));

        var latest = await app.Call<ReceiptFunctions>((f, r, ct) => f.Latest(r, ct), token: r1);
        var item = latest.Json[0];
        Assert.Equal(150.5m, item.GetProperty("totalPayment").GetDecimal());
        Assert.Equal("USD", item.GetProperty("currency").GetString());
        Assert.Equal("Pending", item.GetProperty("status").GetString());
        using (var body = System.Text.Json.JsonDocument.Parse(item.GetProperty("receipt").GetString()!))
        {
            Assert.Equal("2025-03-01T00:00:00", body.RootElement.GetProperty("Date").GetString());
            var fields = body.RootElement.GetProperty("CustomFields");
            Assert.Equal("Maintenance", fields[0].GetProperty("FieldName").GetString());
            Assert.Equal("Amount", fields[0].GetProperty("Type").GetString());
            Assert.Equal("Pay by the 10th", fields[2].GetProperty("Value").GetString());
        }
        Assert.Single((await app.Call<ReceiptFunctions>((f, r, ct) => f.All(r, ct), token: r1)).Json.EnumerateArray());

        // The pending resident is not billed and sees the register prompt.
        Assert.Equal("Register Apaprtment", (await app.Call<ReceiptFunctions>((f, r, ct) => f.Latest(r, ct), token: r2)).Message);

        // Admin marks it paid; the resident sees the new status.
        var forApartment = await app.Call<ReceiptFunctions>((f, id, ct) => f.ForApartment(id, firstId, ct), token: admin);
        var receiptId = forApartment.Json[0].GetProperty("receiptId").GetInt32();
        var paid = await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = firstId, receiptId, status = "Paid" }, admin);
        Assert.True(paid.Ok, paid.Body);
        Assert.Equal("Paid", (await app.Call<ReceiptFunctions>((f, r, ct) => f.Latest(r, ct), token: r1)).Json[0].GetProperty("status").GetString());

        var history = await app.Call<ReceiptFunctions>((f, r, ct) => f.History(r, ct), token: r1);
        Assert.Equal(100m, history.Json[0].GetProperty("receiptValues")[0].GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Profile_round_trips_and_validates()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync(Resident1);

        var initial = await app.Call<UserFunctions>((f, r, ct) => f.GetProfile(r, ct), token: token);
        Assert.Equal("", initial.Json.GetProperty("firstName").GetString());
        Assert.Equal("+96170222222", initial.Json.GetProperty("phoneNumber").GetString());

        Assert.Equal(400, (await app.Call<UserFunctions>((f, r, ct) => f.SaveProfile(r, ct), new { firstName = "", lastName = "X" }, token)).Status);
        Assert.Equal(400, (await app.Call<UserFunctions>((f, r, ct) => f.SaveProfile(r, ct),
            new { firstName = "Rami", lastName = "K", emergencyContactPersonPhoneNumber = "abc" }, token)).Status);

        var save = await app.Call<UserFunctions>((f, r, ct) => f.SaveProfile(r, ct), new
        {
            firstName = " Rami ", lastName = "Khoury", emergencyContactPersonFullName = "Nadia", emergencyContactPersonPhoneNumber = "03 999 888",
        }, token);
        Assert.True(save.Ok, save.Body);

        var profile = await app.Call<UserFunctions>((f, r, ct) => f.GetProfile(r, ct), token: token);
        Assert.Equal("Rami", profile.Json.GetProperty("firstName").GetString());
        Assert.Equal("+9613999888", profile.Json.GetProperty("emergencyContactPersonPhoneNumber").GetString());
    }

    [Fact]
    public async Task Rejected_resident_can_request_again_and_numbers_are_unique_per_building()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync(Admin);
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);
        var r1 = await app.LoginAsync(Resident1);
        var r2 = await app.LoginAsync(Resident2);
        var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: r1)).Json[0].GetProperty("value").GetInt32();

        var request = new { appartmentNumber = "3A", appartmentDescription = "d", floor = 3, buildingId };
        Assert.True((await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), request, r1)).Ok);

        // Same number in the same building is refused; so is a floor above the building's height.
        Assert.Equal(409, (await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), request, r2)).Status);
        Assert.Equal(400, (await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = "9Z", appartmentDescription = "d", floor = 99, buildingId }, r2)).Status);

        // A pending request can't be duplicated.
        Assert.Equal(400, (await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), request, r1)).Status);

        var id = (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json[0].GetProperty("id").GetInt32();
        Assert.True((await app.Call<ApartmentFunctions>((f, r, ct) => f.Reject(r, ct), new { id }, admin)).Ok);
        Assert.Equal("Rejected", (await app.Call<ApartmentFunctions>((f, r, ct) => f.Get(r, ct), token: r1)).Json.GetProperty("status").GetString());

        // Rejected residents can fix their details and ask again.
        Assert.True((await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = "3B", appartmentDescription = "fixed", floor = 3, buildingId }, r1)).Ok);
        Assert.Equal("Pending", (await app.Call<ApartmentFunctions>((f, r, ct) => f.Get(r, ct), token: r1)).Json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Receipts_are_validated_and_scoped_to_the_admins_own_building()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync(Admin);
        var other = await app.LoginAsApprovedAdminAsync("03 444 444");
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);

        object Receipt(string ids, params (string Name, string Type, string Value)[] fields) => new
        {
            currency = "USD",
            appartmentIds = ids,
            receipt = new { currency = "USD", date = "2025-03-01", customFields = fields.Select(f => new { fieldName = f.Name, type = f.Type, value = f.Value }).ToArray() },
        };

        // No approved apartments to bill yet.
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("", ("Maintenance", "Amount", "10")), admin)).Status);

        var resident = await app.LoginAsync(Resident1);
        var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: resident)).Json[0].GetProperty("value").GetInt32();
        await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = "1A", appartmentDescription = "d", floor = 1, buildingId }, resident);
        var aptId = (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json[0].GetProperty("id").GetInt32();
        await app.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id = aptId }, admin);

        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("", ("Maintenance", "Amount", "0")), admin)).Status);          // total must be > 0
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("", ("Maintenance", "Amount", "abc")), admin)).Status);        // bad amount
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("", ("A", "Amount", "1"), ("a", "Text", "x")), admin)).Status);  // duplicate names
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("999", ("Maintenance", "Amount", "10")), admin)).Status);       // unknown apartment
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("1,x", ("Maintenance", "Amount", "10")), admin)).Status);      // garbage ids
        Assert.True((await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt(aptId.ToString(), ("Maintenance", "Amount", "10,5")), admin)).Ok);   // comma decimal

        // Another building's admin can't see or change this building's data.
        Assert.Equal(404, (await app.Call<ReceiptFunctions>((f, id, ct) => f.ForApartment(id, aptId, ct), token: other)).Status);
        var receiptId = (await app.Call<ReceiptFunctions>((f, id, ct) => f.ForApartment(id, aptId, ct), token: admin)).Json[0].GetProperty("receiptId").GetInt32();
        Assert.Equal(404, (await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = aptId, receiptId, status = "Paid" }, other)).Status);
        Assert.Equal(404, (await app.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id = aptId }, other)).Status);
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = aptId, receiptId, status = "Bogus" }, admin)).Status);
    }

    [Fact]
    public async Task Receipt_format_rules_are_enforced()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync(Admin);
        Assert.Equal("SetupBuilding", (await app.Call<ReceiptFunctions>((f, r, ct) => f.GetFormat(r, ct), token: admin)).Message);
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), FormatBody, admin)).Status); // no building yet

        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);
        object Format(string currency, params (string, string)[] fields) =>
            new { currency, customFields = fields.Select(f => new { fieldName = f.Item1, type = f.Item2, value = "" }).ToArray() };

        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), Format("EUR", ("A", "Amount")), admin)).Status);
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), Format("USD", ("A", "Text")), admin)).Status);              // needs an amount
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), Format("USD"), admin)).Status);                          // no fields
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), Format("USD", ("A", "Amount"), ("A", "Text")), admin)).Status);
        Assert.True((await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), Format("lbp", ("A", "amount")), admin)).Ok);
        Assert.True((await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveFormat(r, ct), Format("USD", ("A", "Amount"), ("B", "Text")), admin)).Ok); // update in place
        Assert.Equal(1, await app.Db(db => db.ReceiptFormats.CountAsync()));
    }

    [Fact]
    public async Task Admin_can_remove_an_apartment_and_its_receipts()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync(Admin);
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);
        var resident = await app.LoginAsync(Resident1);
        var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: resident)).Json[0].GetProperty("value").GetInt32();
        await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = "1A", appartmentDescription = "d", floor = 1, buildingId }, resident);
        var id = (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json[0].GetProperty("id").GetInt32();
        await app.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id }, admin);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), new
        {
            currency = "USD", appartmentIds = "",
            receipt = new { currency = "USD", date = "2025-03-01", customFields = new[] { new { fieldName = "M", type = "Amount", value = "5" } } },
        }, admin);
        Assert.Equal(1, await app.Db(db => db.ReceiptAssignments.CountAsync()));

        Assert.True((await app.Call<ApartmentFunctions>((f, r, ct) => f.Delete(r, id, ct), new { }, admin)).Ok);
        Assert.Equal(0, await app.Db(db => db.ReceiptAssignments.CountAsync()));
        Assert.Equal("NoAppartments", (await app.Call<ApartmentFunctions>((f, r, ct) => f.Approved(r, ct), token: admin)).Message);
        Assert.Equal(404, (await app.Call<ApartmentFunctions>((f, r, ct) => f.Delete(r, id, ct), new { }, admin)).Status);
    }
}
