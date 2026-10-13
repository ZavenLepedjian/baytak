using Baytak.Api.Domain;
using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;

namespace Baytak.Api.Tests;

public class WalletTests
{
    private static object Entry(string description = "Coffee", decimal amount = 5, string type = "expense", string? currency = null,
        string? date = "2025-01-02", string? id = null) =>
        new { id, description, amount, type, currency, date };

    [Fact]
    public async Task Empty_wallet_has_zero_balances()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        var wallet = await app.Call<WalletFunctions>((f, r, ct) => f.Get(r, ct), token: token);
        Assert.True(wallet.Ok, wallet.Body);
        Assert.Equal(0m, wallet.Json.GetProperty("balances").GetProperty("USD").GetDecimal());
        Assert.Equal(0m, wallet.Json.GetProperty("balances").GetProperty("LBP").GetDecimal());
        Assert.Equal(0, wallet.Json.GetProperty("transactions").GetArrayLength());
    }

    [Fact]
    public async Task Entries_are_listed_newest_first_with_balances_per_currency()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("Salary", 1500, "income", date: "2025-01-01"), token);
        await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("Groceries", 100.25m, "expense", date: "2025-01-03"), token);
        await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("Rent", 9_000_000m, "expense", "LBP", "2025-01-02"), token);
        await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("Gift", 0.1m, "income", date: "2025-01-04"), token);
        await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("Gift 2", 0.2m, "income", date: "2025-01-04"), token);

        var wallet = await app.Call<WalletFunctions>((f, r, ct) => f.Get(r, ct), token: token);
        var balances = wallet.Json.GetProperty("balances");
        Assert.Equal(1400.05m, balances.GetProperty("USD").GetDecimal()); // exact decimal arithmetic: 1500 - 100.25 + 0.1 + 0.2
        Assert.Equal(-9_000_000m, balances.GetProperty("LBP").GetDecimal());
        Assert.Equal(5, wallet.Json.GetProperty("count").GetInt32());

        var dates = wallet.Json.GetProperty("transactions").EnumerateArray().Select(t => t.GetProperty("date").GetString()).ToList();
        Assert.Equal(dates.OrderByDescending(d => d).ToList(), dates);
    }

    [Fact]
    public async Task Paging_works()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        for (var i = 1; i <= 5; i++)
            await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry($"E{i}", i, date: $"2025-01-0{i}"), token);

        var page1 = await app.Call<WalletFunctions>((f, r, ct) => f.Get(r, ct), token: token, query: "?limit=2");
        Assert.Equal(2, page1.Json.GetProperty("transactions").GetArrayLength());
        Assert.True(page1.Json.GetProperty("hasMore").GetBoolean());
        Assert.Equal("E5", page1.Json.GetProperty("transactions")[0].GetProperty("description").GetString());

        var last = await app.Call<WalletFunctions>((f, r, ct) => f.Get(r, ct), token: token, query: "?limit=2&skip=4");
        Assert.Equal(1, last.Json.GetProperty("transactions").GetArrayLength());
        Assert.False(last.Json.GetProperty("hasMore").GetBoolean());
        // The balance always covers the whole ledger, not just the page.
        Assert.Equal(-15m, last.Json.GetProperty("balances").GetProperty("USD").GetDecimal());
    }

    [Fact]
    public async Task Add_is_idempotent_when_the_client_supplies_an_id()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        var id = Guid.NewGuid().ToString();

        var first = await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry(id: id), token);
        var retry = await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry(id: id), token);
        Assert.Equal(201, first.Status);
        Assert.Equal(200, retry.Status);
        Assert.Equal(first.Json.GetProperty("id").GetString(), retry.Json.GetProperty("id").GetString());
        Assert.Equal(1, await app.Db(db => db.WalletEntries.CountAsync()));
    }

    [Fact]
    public async Task Update_and_delete_work_and_delete_is_idempotent()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        var created = await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("Coffee", 5), token);
        var id = created.Json.GetProperty("id").GetString()!;

        var updated = await app.Call<WalletFunctions>((f, r, ct) => f.Update(r, id, ct), Entry("Latte", 6.5m, "expense", "LBP", "2025-02-02"), token);
        Assert.True(updated.Ok, updated.Body);
        Assert.Equal("Latte", updated.Json.GetProperty("description").GetString());
        Assert.Equal("LBP", updated.Json.GetProperty("currency").GetString());

        Assert.Equal(404, (await app.Call<WalletFunctions>((f, r, ct) => f.Update(r, Guid.NewGuid().ToString(), ct), Entry(), token)).Status);
        Assert.Equal(400, (await app.Call<WalletFunctions>((f, r, ct) => f.Update(r, "not-a-guid", ct), Entry(), token)).Status);
        Assert.Equal(400, (await app.Call<WalletFunctions>((f, r, ct) => f.Update(r, id, ct), Entry(id: Guid.NewGuid().ToString()), token)).Status);

        var deleted = await app.Call<WalletFunctions>((f, r, ct) => f.Delete(r, id, ct), new { }, token);
        Assert.True(deleted.Json.GetProperty("deleted").GetBoolean());
        var again = await app.Call<WalletFunctions>((f, r, ct) => f.Delete(r, id, ct), new { }, token);
        Assert.True(again.Ok);
        Assert.False(again.Json.GetProperty("deleted").GetBoolean());
        Assert.Equal(0, (await app.Call<WalletFunctions>((f, r, ct) => f.Get(r, ct), token: token)).Json.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Users_cannot_see_or_touch_each_others_entries()
    {
        using var app = new TestApp();
        var alice = await app.LoginAsync("03 000 001");
        var bob = await app.LoginAsync("03 000 002");

        var id = (await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("Alice private", 50), alice)).Json.GetProperty("id").GetString()!;

        Assert.Equal(0, (await app.Call<WalletFunctions>((f, r, ct) => f.Get(r, ct), token: bob)).Json.GetProperty("count").GetInt32());
        Assert.Equal(404, (await app.Call<WalletFunctions>((f, r, ct) => f.Update(r, id, ct), Entry("Hacked", 1), bob)).Status);
        Assert.False((await app.Call<WalletFunctions>((f, r, ct) => f.Delete(r, id, ct), new { }, bob)).Json.GetProperty("deleted").GetBoolean());

        // Re-using someone else's id for a new entry must not leak or overwrite it.
        Assert.Equal(409, (await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("Mine now", 1, id: id), bob)).Status);

        var aliceWallet = await app.Call<WalletFunctions>((f, r, ct) => f.Get(r, ct), token: alice);
        Assert.Equal("Alice private", aliceWallet.Json.GetProperty("transactions")[0].GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("", 5, "expense", "USD", "2025-01-01", "Description is required")]
    [InlineData("x", 0, "expense", "USD", "2025-01-01", "greater than 0")]
    [InlineData("x", -5, "expense", "USD", "2025-01-01", "greater than 0")]
    [InlineData("x", 1.234, "expense", "USD", "2025-01-01", "2 decimal")]
    [InlineData("x", 5, "transfer", "USD", "2025-01-01", "income or expense")]
    [InlineData("x", 5, "expense", "EUR", "2025-01-01", "USD or LBP")]
    [InlineData("x", 5, "expense", "USD", "not-a-date", "Date")]
    [InlineData("x", 5, "expense", "USD", "1999-12-31", "out of range")]
    public async Task Invalid_entries_are_rejected_with_a_helpful_message(string description, decimal amount, string type, string currency, string date, string expected)
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        var res = await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry(description, amount, type, currency, date), token);
        Assert.Equal(400, res.Status);
        Assert.Contains(expected, res.Body);
        Assert.Equal(0, await app.Db(db => db.WalletEntries.CountAsync()));
    }

    [Fact]
    public async Task Missing_or_oversized_fields_are_rejected()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        Assert.Equal(400, (await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), new { description = "x", type = "income" }, token)).Status); // no amount
        Assert.Equal(400, (await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), new { description = new string('a', 201), amount = 1, type = "income" }, token)).Status);
        Assert.Equal(400, (await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), new { description = "x", amount = 2_000_000_000_000m, type = "income" }, token)).Status);
        Assert.Equal(400, (await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), rawBody: "[1,2", token: token)).Status);
    }

    [Fact]
    public async Task Amount_may_arrive_as_a_string_and_the_date_may_be_a_full_timestamp()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        var res = await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct),
            rawBody: """{"description":"Tea","amount":"12.50","type":"INCOME","currency":"usd","date":"2025-02-03T23:30:00+03:00"}""", token: token);
        Assert.Equal(201, res.Status);
        Assert.Equal(12.5m, res.Json.GetProperty("amount").GetDecimal());
        Assert.Equal("income", res.Json.GetProperty("type").GetString());
        // The calendar day as written by the phone (UTC+3) is kept; it is not shifted to UTC.
        Assert.Equal("2025-02-03", res.Json.GetProperty("date").GetString());
    }

    [Fact]
    public async Task Control_characters_are_stripped_from_descriptions()
    {
        using var app = new TestApp();
        var token = await app.LoginAsync("03 000 001");
        var res = await app.Call<WalletFunctions>((f, r, ct) => f.Add(r, ct), Entry("  Tea\u0000 \n time  ", 3), token);
        Assert.Equal("Tea time", res.Json.GetProperty("description").GetString());
    }

    [Fact]
    public void Validator_defaults_the_date_to_today_and_currency_to_usd()
    {
        var today = new DateOnly(2025, 6, 15);
        var result = EntryValidator.Validate(new EntryRequest { Description = "x", Amount = 1, Type = "expense" }, today);
        Assert.True(result.IsValid);
        Assert.Equal(today, result.Value!.Date);
        Assert.Equal("USD", result.Value.Currency);
        Assert.Equal(100, result.Value.AmountMinor);
    }
}
