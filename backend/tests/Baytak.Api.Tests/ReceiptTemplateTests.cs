using Baytak.Api.Functions;

namespace Baytak.Api.Tests;

public class ReceiptTemplateTests
{
    private static object Template(string name, string currency = "USD", params string[] amountLines) => new
    {
        name, currency,
        customFields = amountLines.Select(n => new { fieldName = n, type = "Amount", value = "" })
            .Append(new { fieldName = "Note", type = "Text", value = "" }).ToArray(),
    };

    [Fact]
    public async Task Templates_are_saved_replaced_by_name_listed_and_deleted_per_building()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync("03 111 111");
        Assert.Equal(0, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.List(r, ct), token: admin)).Json.GetProperty("templates").GetArrayLength());

        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), SeedHelpers.BuildingBody, admin);
        Assert.True((await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("Monthly fees", "USD", "Maintenance", "Electricity"), admin)).Ok);
        Assert.True((await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("Generator", "LBP", "Generator"), admin)).Ok);

        // The same name (any case) replaces rather than duplicates.
        Assert.True((await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("monthly FEES", "USD", "Maintenance", "Water"), admin)).Ok);

        var list = (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.List(r, ct), token: admin)).Json.GetProperty("templates").EnumerateArray().ToList();
        Assert.Equal(new[] { "Generator", "monthly FEES" }, list.Select(t => t.GetProperty("name").GetString()).ToArray());
        var monthly = list[1];
        Assert.Equal(new[] { "Maintenance", "Water", "Note" }, monthly.GetProperty("customFields").EnumerateArray().Select(f => f.GetProperty("fieldName").GetString()).ToArray());
        Assert.Equal("LBP", list[0].GetProperty("currency").GetString());

        // Another building's administrator sees none of them and cannot delete them.
        var other = await app.LoginAsApprovedAdminAsync("03 444 444");
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), SeedHelpers.BuildingBody, other);
        Assert.Equal(0, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.List(r, ct), token: other)).Json.GetProperty("templates").GetArrayLength());
        var id = monthly.GetProperty("id").GetInt32();
        Assert.Equal(404, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Delete(r, id, ct), new { }, other)).Status);

        Assert.True((await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Delete(r, id, ct), new { }, admin)).Ok);
        Assert.Equal(1, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.List(r, ct), token: admin)).Json.GetProperty("templates").GetArrayLength());
    }

    [Fact]
    public async Task Templates_are_validated_and_capped()
    {
        using var app = new TestApp();
        var admin = await app.LoginAsApprovedAdminAsync("03 111 111");
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), SeedHelpers.BuildingBody, admin);

        Assert.Equal(400, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("  ", "USD", "Maintenance"), admin)).Status);
        Assert.Equal(400, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template(new string('x', 61), "USD", "Maintenance"), admin)).Status);
        Assert.Equal(400, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("Bad currency", "EUR", "Maintenance"), admin)).Status);
        Assert.Equal(400, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("No amounts", "USD"), admin)).Status);
        Assert.Equal(400, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("Same name twice", "USD", "Water", "water"), admin)).Status);

        var resident = await app.LoginAsync("70 000 009");
        Assert.Equal(403, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("Mine", "USD", "Water"), resident)).Status);

        for (var i = 0; i < ReceiptTemplateFunctions.MaxTemplates; i++)
            Assert.True((await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template($"T{i}", "USD", "Water"), admin)).Ok);
        Assert.Equal(400, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("One too many", "USD", "Water"), admin)).Status);
        Assert.True((await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.Save(r, ct), Template("t0", "LBP", "Water"), admin)).Ok); // replacing is still allowed at the cap
    }

    [Fact]
    public async Task The_last_receipt_comes_back_with_its_lines_and_amounts()
    {
        using var app = new TestApp();
        var (admin, _, _) = await SeedHelpers.BuildingWithResidentsAsync(app);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.List(r, ct), token: admin)).Json.GetProperty("last").ValueKind);

        // Two receipts: the newer by date is "last", even though it was issued first.
        foreach (var (date, amount, note) in new[] { ("2025-03-01", "120", "March"), ("2025-02-01", "100", "February") })
        {
            var add = await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), new
            {
                currency = "USD", appartmentIds = "",
                receipt = new { currency = "USD", date, customFields = new object[] { new { fieldName = "Maintenance", type = "Amount", value = amount }, new { fieldName = "Note", type = "Text", value = note } } },
            }, admin);
            Assert.True(add.Ok, add.Body);
        }

        var last = (await app.Call<ReceiptTemplateFunctions>((f, r, ct) => f.List(r, ct), token: admin)).Json.GetProperty("last");
        Assert.Equal("2025-03-01", last.GetProperty("date").GetString());
        Assert.Equal("USD", last.GetProperty("currency").GetString());
        var lines = last.GetProperty("customFields").EnumerateArray().ToList();
        Assert.Equal(("Maintenance", "Amount", "120"), (lines[0].GetProperty("fieldName").GetString(), lines[0].GetProperty("type").GetString(), lines[0].GetProperty("value").GetString()));
        Assert.Equal(("Note", "Text", "March"), (lines[1].GetProperty("fieldName").GetString(), lines[1].GetProperty("type").GetString(), lines[1].GetProperty("value").GetString()));
    }
}
