using Baytak.Api.Functions;

namespace Baytak.Api.Tests;

public class SummaryTests
{
    private static object Receipt(string currency, string amount, string date) => new
    {
        currency, appartmentIds = "",
        receipt = new { currency, date, customFields = new[] { new { fieldName = "Maintenance", type = "Amount", value = amount } } },
    };

    private static Task<Response> SummaryAsync(TestApp app, string token, string? month = null) =>
        app.Call<SummaryFunctions>((f, r, ct) => f.Summary(r, ct), token: token, query: month is null ? null : "?month=" + month);

    private static System.Text.Json.JsonElement Block(Response summary, string currency) =>
        summary.Json.GetProperty("currencies").EnumerateArray().Single(c => c.GetProperty("currency").GetString() == currency);

    [Fact]
    public async Task The_summary_counts_each_month_on_its_own_and_never_mixes_currencies()
    {
        using var app = new TestApp();
        var (admin, ids, _) = await SeedHelpers.BuildingWithResidentsAsync(app);
        var (apt1A, apt2A, _) = (ids[0], ids[1], ids[2]);

        // 10 March: 100 USD to all three apartments, and a 2,000,000 LBP receipt. 1A pays the dollars on the 20th.
        app.Time.SetUtcNow(new DateTimeOffset(2025, 3, 10, 9, 0, 0, TimeSpan.Zero));
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("USD", "100", "2025-03-10"), admin);
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("LBP", "2000000", "2025-03-12"), admin);
        var receipts = (await app.Call<CollectionFunctions>((f, r, ct) => f.Summary(r, ct), token: admin))
            .Json.GetProperty("receipts").EnumerateArray().ToDictionary(r => r.GetProperty("date").GetString()!, r => r.GetProperty("id").GetInt32());
        var dollars = receipts["2025-03-10"];
        app.Time.SetUtcNow(new DateTimeOffset(2025, 3, 20, 9, 0, 0, TimeSpan.Zero));
        await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = apt1A, receiptId = dollars, status = "Paid" }, admin);

        // 5 April: 2A pays the March dollar receipt late.
        app.Time.SetUtcNow(new DateTimeOffset(2025, 4, 5, 9, 0, 0, TimeSpan.Zero));
        // The clock moved a month, so the first tokens have expired: sign in again, as people do.
        admin = await app.LoginAsync("03 111 111");
        var resident = await app.LoginAsync("70 000 001");
        var paid = await app.Call<ReceiptFunctions>((f, r, ct) => f.SaveStatus(r, ct), new { appartmentId = apt2A, receiptId = dollars, status = "Paid" }, admin);
        Assert.True(paid.Ok, paid.Body);

        var march = await SummaryAsync(app, admin, "2025-03");
        Assert.True(march.Ok, march.Body);
        Assert.Equal("2025-03", march.Json.GetProperty("month").GetString());
        Assert.Equal("Tower", march.Json.GetProperty("buildingName").GetString());
        Assert.True(march.Json.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(2, march.Json.GetProperty("receiptsIssued").GetInt32());
        Assert.Equal(new[] { "USD", "LBP" }, march.Json.GetProperty("currencies").EnumerateArray().Select(c => c.GetProperty("currency").GetString()).ToArray());

        var usd = Block(march, "USD");
        Assert.Equal(300m, usd.GetProperty("billed").GetDecimal());
        Assert.Equal(100m, usd.GetProperty("collected").GetDecimal());          // only 1A had paid by the end of March
        Assert.Equal(200m, usd.GetProperty("outstanding").GetDecimal());        // 2A's April payment does not change what was owed on 31 March
        Assert.Equal(2, usd.GetProperty("outstandingApartments").GetInt32());
        var lira = Block(march, "LBP");
        Assert.Equal(6000000m, lira.GetProperty("billed").GetDecimal());
        Assert.Equal(0m, lira.GetProperty("collected").GetDecimal());
        Assert.Equal(6000000m, lira.GetProperty("outstanding").GetDecimal());

        var april = await SummaryAsync(app, admin, "2025-04");
        var aprilUsd = Block(april, "USD");
        Assert.Equal(0m, aprilUsd.GetProperty("billed").GetDecimal());
        Assert.Equal(100m, aprilUsd.GetProperty("collected").GetDecimal());     // 2A's payment lands in April
        Assert.Equal(100m, aprilUsd.GetProperty("outstanding").GetDecimal());   // only 2B still owes
        Assert.Equal(1, aprilUsd.GetProperty("outstandingApartments").GetInt32());
        Assert.Equal(0, april.Json.GetProperty("receiptsIssued").GetInt32());

        // A month before the first receipt has nothing in it.
        var before = await SummaryAsync(app, admin, "2025-01");
        Assert.Equal(0, before.Json.GetProperty("currencies").GetArrayLength());

        // No month, or a made-up one, means the current month.
        Assert.Equal("2025-04", (await SummaryAsync(app, admin)).Json.GetProperty("month").GetString());
        Assert.Equal("2025-04", (await SummaryAsync(app, admin, "banana")).Json.GetProperty("month").GetString());
        Assert.Equal("2025-03", march.Json.GetProperty("earliest").GetString());

        // A resident of the building reads the same figures, and is not an administrator.
        var asResident = await SummaryAsync(app, resident, "2025-03");
        Assert.True(asResident.Ok, asResident.Body);
        Assert.False(asResident.Json.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(100m, Block(asResident, "USD").GetProperty("collected").GetDecimal());
        Assert.DoesNotContain("Res1A", asResident.Body);                          // no names of who paid or owes
        Assert.DoesNotContain("apartmentNumber", asResident.Body);
    }

    [Fact]
    public async Task One_building_cannot_read_another_buildings_summary()
    {
        using var app = new TestApp();
        var (admin, _, _) = await SeedHelpers.BuildingWithResidentsAsync(app);
        var (other, _, otherResidents) = await SeedHelpers.BuildingWithResidentsAsync(app, "03 222 222", "71 000 00");
        Assert.True((await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), Receipt("USD", "100", "2025-02-20"), admin)).Ok);

        foreach (var token in new[] { other, otherResidents[0] })
        {
            var summary = await SummaryAsync(app, token, "2025-02");
            Assert.Equal(0, summary.Json.GetProperty("receiptsIssued").GetInt32());
            Assert.Equal(0, summary.Json.GetProperty("currencies").GetArrayLength());
        }
    }

    [Fact]
    public async Task People_without_a_building_get_an_empty_summary()
    {
        using var app = new TestApp();
        await SeedHelpers.BuildingWithResidentsAsync(app);

        var stranger = await app.LoginAsync("70 999 999");
        var none = await SummaryAsync(app, stranger);
        Assert.True(none.Ok, none.Body);
        Assert.False(none.Json.GetProperty("hasBuilding").GetBoolean());

        // A resident still waiting for approval is not yet part of the building.
        var waiting = await app.LoginAsync("70 888 888");
        var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: waiting)).Json.EnumerateArray().Last().GetProperty("value").GetInt32();
        Assert.True((await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = "9Z", appartmentDescription = "d", floor = 3, buildingId }, waiting)).Ok);
        Assert.False((await SummaryAsync(app, waiting)).Json.GetProperty("hasBuilding").GetBoolean());

        Assert.Equal(401, (await SummaryAsync(app, "nope")).Status);
    }
}
