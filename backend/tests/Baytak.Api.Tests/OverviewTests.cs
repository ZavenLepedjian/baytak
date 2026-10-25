using Baytak.Api.Functions;

namespace Baytak.Api.Tests;

public class OverviewTests
{
    private const string Admin = "03 111 111";

    private static readonly object BuildingBody = new
    {
        buildingNumber = "1", buildingName = "Tower", street = "Main", city = "Beirut", country = "LB",
        numberOfFloors = 6, latitude = 33.9, longitude = 35.5,
    };

    private static object Receipt(string currency, string amount, string date) => new
    {
        currency, appartmentIds = "",
        receipt = new { currency, date, customFields = new[] { new { fieldName = "Maintenance", type = "Amount", value = amount } } },
    };

    /// <summary>An approved building with 1A (floor 1), 2A and 2B (floor 2), plus one join request still waiting (3A).</summary>
    private static async Task<(string Admin, int[] Ids)> SeedAsync(TestApp app)
    {
        var admin = await app.LoginAsApprovedAdminAsync(Admin);
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);

        var specs = new[] { ("70 000 001", "1A", 1), ("70 000 002", "2A", 2), ("70 000 003", "2B", 2), ("70 000 004", "3A", 3) };
        foreach (var (phone, number, floor) in specs)
        {
            var token = await app.LoginAsync(phone);
            var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: token)).Json[0].GetProperty("value").GetInt32();
            await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = number, appartmentDescription = "d", floor, buildingId }, token);
            await app.Call<UserFunctions>((f, r, ct) => f.SaveProfile(r, ct), new { firstName = $"Res{number}", lastName = "Test" }, token);
        }

        var ids = new List<int>();
        foreach (var a in (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json.EnumerateArray().ToList())
        {
            if (a.GetProperty("appartmentNumber").GetString() == "3A") continue; // stays pending
            var id = a.GetProperty("id").GetInt32();
            ids.Add(id);
            await app.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id }, admin);
        }
        return (admin, ids.ToArray());
    }

    private static async Task<Dictionary<string, int>> ReceiptIdsByDateAsync(TestApp app, string admin)
    {
        var summary = await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin);
        return summary.Json.GetProperty("receipts").EnumerateArray()
            .ToDictionary(r => r.GetProperty("date").GetString()!, r => r.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Overview_is_empty_but_valid_before_a_building_or_any_receipt_and_closed_to_residents()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync(Admin);

        var none = await app.Call<OverviewFunctions>((f, r, ct) => f.Overview(r, ct), token: admin);
        Assert.True(none.Ok, none.Body);
        Assert.False(none.Json.GetProperty("hasBuilding").GetBoolean());

        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);
        var empty = await app.Call<OverviewFunctions>((f, r, ct) => f.Overview(r, ct), token: admin);
        Assert.True(empty.Json.GetProperty("hasBuilding").GetBoolean());
        Assert.Equal("Tower", empty.Json.GetProperty("buildingName").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, empty.Json.GetProperty("latest").ValueKind);
        foreach (var list in new[] { "outstanding", "needsAttention", "recentPayments", "receipts" })
            Assert.Equal(0, empty.Json.GetProperty(list).GetArrayLength());

        var resident = await app.LoginAsync("70 000 009");
        Assert.Equal(403, (await app.Call<OverviewFunctions>((f, r, ct) => f.Overview(r, ct), token: resident)).Status);
    }

    [Fact]
    public async Task Overview_totals_what_is_owed_per_currency_and_ranks_late_apartments()
    {
        using var app = new TestApp(); // the clock starts on 2025-03-01
        var (admin, ids) = await SeedAsync(app);

        // A: 100 USD from January, issued to all three. B: 4,000,000 LBP from last week, issued to all three.
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("USD", "100", "2025-01-01"), admin);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("LBP", "4000000", "2025-02-25"), admin);
        var receipts = await ReceiptIdsByDateAsync(app, admin);
        var a = receipts["2025-01-01"];
        var b = receipts["2025-02-25"];
        var (apt1A, apt2A, apt2B) = (ids[0], ids[1], ids[2]);

        // 1A and 2A pay the January receipt on 1 March (59 days late); 2B pays the recent one on time. One minute apart so the order is known.
        await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = apt1A, receiptId = a, status = "Paid" }, admin);
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = apt2A, receiptId = a, status = "Paid" }, admin);
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = apt2B, receiptId = b, status = "Paid" }, admin);

        var overview = await app.Call<OverviewFunctions>((f, r, ct) => f.Overview(r, ct), token: admin);
        Assert.True(overview.Ok, overview.Body);
        var json = overview.Json;

        Assert.Equal(3, json.GetProperty("apartmentCount").GetInt32());
        Assert.Equal(1, json.GetProperty("pendingRequests").GetInt32());
        Assert.Equal(OverviewFunctions.LateAfterDays, json.GetProperty("lateAfterDays").GetInt32());

        // The newest receipt (by date) is the lira one: 1 of 3 paid.
        var latest = json.GetProperty("latest");
        Assert.Equal(b, latest.GetProperty("id").GetInt32());
        Assert.Equal("LBP", latest.GetProperty("currency").GetString());
        Assert.Equal(1, latest.GetProperty("paidCount").GetInt32());
        Assert.Equal(2, latest.GetProperty("unpaidCount").GetInt32());
        Assert.Equal(4000000m, latest.GetProperty("collected").GetDecimal());
        Assert.Equal(12000000m, latest.GetProperty("expected").GetDecimal());

        // Still owed: 2B has not paid the 100 USD; 1A and 2A have not paid the lira receipt. Dollars first, never mixed.
        var owed = json.GetProperty("outstanding").EnumerateArray().ToList();
        Assert.Equal(new[] { "USD", "LBP" }, owed.Select(o => o.GetProperty("currency").GetString()).ToArray());
        Assert.Equal(100m, owed[0].GetProperty("amount").GetDecimal());
        Assert.Equal(1, owed[0].GetProperty("apartments").GetInt32());
        Assert.Equal(8000000m, owed[1].GetProperty("amount").GetDecimal());
        Assert.Equal(2, owed[1].GetProperty("apartments").GetInt32());

        // Attention: 2B is 59 days overdue; 1A and 2A paid the January receipt 59 days late (and still owe the lira one, 4 days old).
        var attention = json.GetProperty("needsAttention").EnumerateArray().ToList();
        Assert.Equal(new[] { "2B", "1A", "2A" }, attention.Select(x => x.GetProperty("apartmentNumber").GetString()).ToArray());
        Assert.Equal(1, attention[0].GetProperty("overdueCount").GetInt32());
        Assert.Equal(59, attention[0].GetProperty("oldestUnpaidDays").GetInt32());
        Assert.Equal(0, attention[0].GetProperty("latePaidCount").GetInt32());
        Assert.Equal("Res2B Test", attention[0].GetProperty("residentName").GetString());
        Assert.Equal(1, attention[1].GetProperty("latePaidCount").GetInt32());
        Assert.Equal(0, attention[1].GetProperty("overdueCount").GetInt32()); // 4 days old is not overdue yet
        var owedBy1A = attention[1].GetProperty("owed").EnumerateArray().Single();
        Assert.Equal("LBP", owedBy1A.GetProperty("currency").GetString());
        Assert.Equal(4000000m, owedBy1A.GetProperty("amount").GetDecimal());

        // Recent payments, newest first, each in its own currency.
        var payments = json.GetProperty("recentPayments").EnumerateArray().ToList();
        Assert.Equal(new[] { "2B", "2A", "1A" }, payments.Select(p => p.GetProperty("apartmentNumber").GetString()).ToArray());
        Assert.Equal("LBP", payments[0].GetProperty("currency").GetString());
        Assert.Equal(4000000m, payments[0].GetProperty("amount").GetDecimal());
        Assert.Equal("USD", payments[1].GetProperty("currency").GetString());

        // The receipt list is the newest first, with how many paid each.
        var list = json.GetProperty("receipts").EnumerateArray().ToList();
        Assert.Equal(new[] { b, a }, list.Select(r => r.GetProperty("id").GetInt32()).ToArray());
        Assert.Equal(2, list[1].GetProperty("paidCount").GetInt32());
    }

    [Fact]
    public async Task Overview_shows_only_the_callers_own_building()
    {
        using var app = new TestApp();
        var (admin, _) = await SeedAsync(app);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("USD", "100", "2025-01-01"), admin);

        var other = await app.LoginAsApprovedAdminAsync("03 444 444");
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, other);
        var overview = await app.Call<OverviewFunctions>((f, r, ct) => f.Overview(r, ct), token: other);

        Assert.True(overview.Ok, overview.Body);
        Assert.Equal(0, overview.Json.GetProperty("apartmentCount").GetInt32());
        Assert.Equal(0, overview.Json.GetProperty("outstanding").GetArrayLength());
        Assert.Equal(0, overview.Json.GetProperty("needsAttention").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, overview.Json.GetProperty("latest").ValueKind);
    }
}
