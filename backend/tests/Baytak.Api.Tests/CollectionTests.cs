using Baytak.Api.Data;
using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;

namespace Baytak.Api.Tests;

public class CollectionTests
{
    private const string Admin = "03 111 111";

    private static readonly object BuildingBody = new
    {
        buildingNumber = "1", buildingName = "Tower", street = "Main", city = "Beirut", country = "LB",
        numberOfFloors = 6, latitude = 33.9, longitude = 35.5,
    };

    private static object Receipt(string amount, string date = "2025-03-01") => new
    {
        currency = "USD", appartmentIds = "",
        receipt = new { currency = "USD", date, customFields = new[] { new { fieldName = "Maintenance", type = "Amount", value = amount } } },
    };

    /// <summary>An approved building with three residents: 1A (floor 1), 2A and 2B (floor 2). Returns the admin token and apartment ids.</summary>
    private static async Task<(string Admin, int[] Ids, string[] Residents)> SeedAsync(TestApp app)
    {
        var admin = await app.LoginAsApprovedAdminAsync(Admin);
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);

        var specs = new[] { ("70 000 001", "1A", 1), ("70 000 002", "2A", 2), ("70 000 003", "2B", 2) };
        var residents = new List<string>();
        var ids = new List<int>();
        foreach (var (phone, number, floor) in specs)
        {
            var token = await app.LoginAsync(phone);
            residents.Add(token);
            var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: token)).Json[0].GetProperty("value").GetInt32();
            await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = number, appartmentDescription = "d", floor, buildingId }, token);
            await app.Call<UserFunctions>((f, r, ct) => f.SaveProfile(r, ct), new { firstName = $"Res{number}", lastName = "Test" }, token);
        }
        foreach (var a in (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json.EnumerateArray())
        {
            var id = a.GetProperty("id").GetInt32();
            ids.Add(id);
            await app.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id }, admin);
        }
        return (admin, ids.ToArray(), residents.ToArray());
    }

    [Fact]
    public async Task Before_a_building_or_any_receipt_the_summary_is_empty_but_valid()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync(Admin);

        var noBuilding = await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin);
        Assert.False(noBuilding.Json.GetProperty("hasBuilding").GetBoolean());

        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);
        var noReceipts = await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin);
        Assert.True(noReceipts.Json.GetProperty("hasBuilding").GetBoolean());
        Assert.Equal(0, noReceipts.Json.GetProperty("receipts").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, noReceipts.Json.GetProperty("selected").ValueKind);
    }

    [Fact]
    public async Task Summary_shows_who_paid_the_chosen_receipt_ordered_by_floor()
    {
        using var app = new TestApp();
        var (admin, ids, _) = await SeedAsync(app);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("100"), admin);

        var receiptId = (await app.Call<ReceiptFunctions>((f, id, ct) => f.ForApartment(id, ids[0], ct), token: admin)).Json[0].GetProperty("receiptId").GetInt32();
        await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = ids[1], receiptId, status = "Paid" }, admin);

        var summary = await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin);
        Assert.True(summary.Ok, summary.Body);
        var selected = summary.Json.GetProperty("selected");
        Assert.Equal(receiptId, selected.GetProperty("id").GetInt32());
        Assert.Equal(3, selected.GetProperty("count").GetInt32());
        Assert.Equal(1, selected.GetProperty("paidCount").GetInt32());
        Assert.Equal(2, selected.GetProperty("unpaidCount").GetInt32());
        Assert.Equal(100m, selected.GetProperty("collected").GetDecimal());
        Assert.Equal(300m, selected.GetProperty("expected").GetDecimal());
        Assert.True(selected.GetProperty("canRemind").GetBoolean());

        var plates = selected.GetProperty("apartments").EnumerateArray().ToList();
        Assert.Equal(new[] { "1A", "2A", "2B" }, plates.Select(p => p.GetProperty("apartmentNumber").GetString()).ToArray());
        Assert.Equal("Res2A Test", plates[1].GetProperty("residentName").GetString());
        Assert.Equal("Paid", plates[1].GetProperty("status").GetString());
        Assert.Equal("Pending", plates[0].GetProperty("status").GetString());

        // The switcher lists the receipt with its paid count.
        var first = summary.Json.GetProperty("receipts")[0];
        Assert.Equal(1, first.GetProperty("paidCount").GetInt32());
        Assert.Equal(3, first.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Receipts_can_be_switched_and_are_limited_to_the_buildings_own()
    {
        using var app = new TestApp();
        var (admin, _, _) = await SeedAsync(app);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("100", "2025-02-01"), admin);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("120", "2025-03-01"), admin);

        var summary = await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin);
        var receipts = summary.Json.GetProperty("receipts").EnumerateArray().ToList();
        Assert.Equal(new[] { "2025-03-01", "2025-02-01" }, receipts.Select(r => r.GetProperty("date").GetString()).ToArray());
        Assert.Equal(120m, summary.Json.GetProperty("selected").GetProperty("total").GetDecimal());

        var older = receipts[1].GetProperty("id").GetInt32();
        var chosen = await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin, query: $"?receiptId={older}");
        Assert.Equal(100m, chosen.Json.GetProperty("selected").GetProperty("total").GetDecimal());

        // Another building's administrator can neither read this receipt nor remind about it.
        var other = await app.LoginAsApprovedAdminAsync("03 444 444");
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, other);
        Assert.Equal(404, (await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: other, query: $"?receiptId={older}")).Status);
        Assert.Equal(404, (await app.Call<CollectionFunctions>((f, r, ct) => f.Remind(r, ct), new { receiptId = older }, other)).Status);
        Assert.Equal(400, (await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin, query: "?receiptId=abc")).Status);
    }

    [Fact]
    public async Task Residents_cannot_use_collection_endpoints()
    {
        using var app = new TestApp();
        var resident = await app.LoginAsync("70 000 009");
        Assert.Equal(403, (await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: resident)).Status);
        Assert.Equal(403, (await app.Call<CollectionFunctions>((f, r, ct) => f.Remind(r, ct), new { receiptId = 1 }, resident)).Status);
    }

    [Fact]
    public async Task Reminder_goes_only_to_unpaid_residents_once_a_day()
    {
        using var app = new TestApp();
        var (admin, ids, residents) = await SeedAsync(app);
        for (var i = 0; i < residents.Length; i++)
            await app.Call<UserFunctions>((f, r, ct) => f.SetupNotification(r, ct), new { notificationToken = $"ExponentPushToken[resident-token-{i}]" }, residents[i]);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("100"), admin);
        var receiptId = (await app.Call<ReceiptFunctions>((f, id, ct) => f.ForApartment(id, ids[0], ct), token: admin)).Json[0].GetProperty("receiptId").GetInt32();
        app.Push.Sent.Clear();

        // The first resident (1A) pays; 2A and 2B stay unpaid.
        await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = ids[0], receiptId, status = "Paid" }, admin);

        var remind = await app.Call<CollectionFunctions>((f, r, ct) => f.Remind(r, ct), new { receiptId }, admin);
        Assert.True(remind.Ok, remind.Body);
        Assert.Equal(2, remind.Json.GetProperty("sent").GetInt32());

        var push = Assert.Single(app.Push.Sent);
        Assert.Equal("Payment reminder", push.Title);
        Assert.DoesNotContain("ExponentPushToken[resident-token-0]", push.Tokens);
        Assert.Contains("ExponentPushToken[resident-token-1]", push.Tokens);
        Assert.Contains("ExponentPushToken[resident-token-2]", push.Tokens);

        // A second reminder the same day is refused, and the summary says so; the next day it is allowed again.
        Assert.Equal(429, (await app.Call<CollectionFunctions>((f, r, ct) => f.Remind(r, ct), new { receiptId }, admin)).Status);
        var summary = await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin);
        Assert.False(summary.Json.GetProperty("selected").GetProperty("canRemind").GetBoolean());
        app.Time.Advance(TimeSpan.FromHours(25));
        Assert.True((await app.Call<CollectionFunctions>((f, r, ct) => f.Remind(r, ct), new { receiptId }, admin)).Ok);
    }

    [Fact]
    public async Task Reminder_is_refused_when_everyone_has_paid()
    {
        using var app = new TestApp();
        var (admin, ids, _) = await SeedAsync(app);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("100"), admin);
        var receiptId = (await app.Call<ReceiptFunctions>((f, id, ct) => f.ForApartment(id, ids[0], ct), token: admin)).Json[0].GetProperty("receiptId").GetInt32();
        foreach (var id in ids)
            await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = id, receiptId, status = "Paid" }, admin);

        var summary = await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin);
        Assert.False(summary.Json.GetProperty("selected").GetProperty("canRemind").GetBoolean());
        var remind = await app.Call<CollectionFunctions>((f, r, ct) => f.Remind(r, ct), new { receiptId }, admin);
        Assert.Equal(400, remind.Status);
        Assert.Contains("Everyone has paid", remind.Body);
        Assert.Equal(0, await app.Db(db => db.Receipts.CountAsync(r => r.LastReminderAt != null)));
    }
}
