using System.Text.Json;
using Baytak.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Baytak.Mcp.Tests;

/// <summary>The MCP protocol and every tool, against a seeded building.</summary>
public class ToolTests
{
    [Fact]
    public async Task Initialize_negotiates_the_protocol_and_notifications_get_202()
    {
        using var host = new McpHost();
        var connection = await host.ConnectAsync(McpHost.ResidentPhone);

        var init = await host.RpcAsync(connection.AccessToken, "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } });
        Assert.Equal("2025-06-18", init.GetProperty("protocolVersion").GetString());
        Assert.Equal("baytak", init.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Contains("never add or convert amounts across currencies", init.GetProperty("instructions").GetString());

        var unknownVersion = await host.RpcAsync(connection.AccessToken, "initialize", new { protocolVersion = "1999-01-01" });
        Assert.Equal("2025-11-25", unknownVersion.GetProperty("protocolVersion").GetString());

        var note = await host.Mcp(connection.AccessToken, new { jsonrpc = "2.0", method = "notifications/initialized" });
        Assert.Equal(202, note.Status);

        var missing = await host.Mcp(connection.AccessToken, new { jsonrpc = "2.0", id = 9, method = "does/not/exist" });
        Assert.Equal(-32601, missing.Json.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Every_account_sees_all_twelve_tools()
    {
        // ChatGPT keeps one tool list per app, so the list must not depend on who is signed in.
        using var host = new McpHost();
        await host.SeedBuildingAsync();

        var resident = await host.ConnectAsync(McpHost.ResidentPhone);
        var residentTools = Names(await host.RpcAsync(resident.AccessToken, "tools/list"));
        var admin = await host.ConnectAsync(McpHost.AdminPhone);
        var adminTools = Names(await host.RpcAsync(admin.AccessToken, "tools/list"));

        Assert.Equal(12, residentTools.Count);
        Assert.Equal(adminTools.Order().ToArray(), residentTools.Order().ToArray());
        Assert.Contains("issue_receipt", residentTools);
        Assert.Contains("get_collection_status", residentTools);
        Assert.Contains("get_my_balance", adminTools);
    }

    [Fact]
    public async Task Tool_hints_follow_the_openai_guidelines()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var admin = await host.ConnectAsync(McpHost.AdminPhone);
        var tools = (await host.RpcAsync(admin.AccessToken, "tools/list")).GetProperty("tools").EnumerateArray().ToList();
        var writes = new[] { "set_payment_status", "send_payment_reminder", "approve_join_request", "reject_join_request", "issue_receipt" };

        Assert.Equal(12, tools.Count);
        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString()!;
            var hints = tool.GetProperty("annotations");
            Assert.False(hints.GetProperty("openWorldHint").GetBoolean(), $"{name} stays inside the person's own building");
            if (writes.Contains(name))
            {
                Assert.False(hints.GetProperty("readOnlyHint").GetBoolean(), name);
                Assert.True(hints.GetProperty("destructiveHint").GetBoolean(), $"{name} overwrites state or notifies people");
            }
            else
            {
                Assert.True(hints.GetProperty("readOnlyHint").GetBoolean(), name);
                Assert.False(hints.GetProperty("destructiveHint").GetBoolean(), name);
            }
        }
        var issue = tools.Single(t => t.GetProperty("name").GetString() == "issue_receipt");
        var description = issue.GetProperty("description").GetString();
        Assert.Contains("never repeat it for receipts that were already issued", description);
        Assert.Contains("two receipts in one call", description);
    }

    [Fact]
    public async Task A_resident_asks_what_they_owe()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var resident = await host.ConnectAsync(McpHost.ResidentPhone);

        var balance = await host.CallToolAsync(resident.AccessToken, "get_my_balance");
        Assert.False(McpHost.IsError(balance));
        Assert.StartsWith("You owe 150.50 USD across 1 unpaid receipt", McpHost.Text(balance));
        Assert.Contains("Oct 1, 2026", McpHost.Text(balance));
        Assert.Contains("Maintenance 100, Electricity 50.50, Note: Pay by the 10th", McpHost.Text(balance));
        var owed = balance.GetProperty("structuredContent").GetProperty("owed")[0];
        Assert.Equal("USD", owed.GetProperty("currency").GetString());
        Assert.Equal(150.5m, owed.GetProperty("amount").GetDecimal());

        var apartment = await host.CallToolAsync(resident.AccessToken, "get_my_apartment");
        Assert.Equal("Apartment 3A, 3rd floor, Cedar Residence. You're signed in as a resident.", McpHost.Text(apartment));

        var paid = await host.CallToolAsync(resident.AccessToken, "list_my_receipts", new { status = "paid" });
        Assert.Equal("No paid receipts.", McpHost.Text(paid));
    }

    [Fact]
    public async Task Someone_who_has_not_joined_is_told_what_to_do()
    {
        using var host = new McpHost();
        var stranger = await host.ConnectAsync("76 444 444");
        var balance = await host.CallToolAsync(stranger.AccessToken, "get_my_balance");
        Assert.False(McpHost.IsError(balance));
        Assert.StartsWith("You haven't joined a building yet.", McpHost.Text(balance));
    }

    [Fact]
    public async Task A_resident_cannot_use_building_tools_even_by_name()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var resident = await host.ConnectAsync(McpHost.ResidentPhone);

        var result = await host.CallToolAsync(resident.AccessToken, "set_payment_status", new { apartments = new[] { "3A" }, status = "paid" });
        Assert.True(McpHost.IsError(result));
        Assert.Equal("Administrator access is required.", McpHost.Text(result));
        Assert.Equal(0, await host.Api.Db(db => db.ReceiptAssignments.CountAsync(a => a.Status == PaymentStatuses.Paid)));
    }

    [Fact]
    public async Task An_admin_checks_collection_marks_paid_and_reminds()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var admin = await host.ConnectAsync(McpHost.AdminPhone);

        var status = await host.CallToolAsync(admin.AccessToken, "get_collection_status");
        Assert.StartsWith("Oct 1, 2026 receipt, 150.50 USD per apartment: 0 of 2 apartments have paid. Collected 0 USD of 301 USD.", McpHost.Text(status));
        Assert.DoesNotContain("70 222 222", status.ToString()); // phone numbers stay in the app
        Assert.DoesNotContain("paidAt", status.ToString()); // the answer does not need payment timestamps
        Assert.DoesNotContain("+96170222222", status.ToString());

        var marked = await host.CallToolAsync(admin.AccessToken, "set_payment_status", new { apartments = new[] { "3a" }, status = "paid" });
        Assert.Equal("Marked 3A as paid on the Oct 1, 2026 receipt.", McpHost.Text(marked));

        var unknown = await host.CallToolAsync(admin.AccessToken, "set_payment_status", new { apartments = new[] { "9Z" }, status = "paid" });
        Assert.True(McpHost.IsError(unknown));
        Assert.Contains("Apartments on it: 3A, 5B", McpHost.Text(unknown));

        var after = await host.CallToolAsync(admin.AccessToken, "get_collection_status");
        Assert.Contains("1 of 2 apartments have paid. Collected 150.50 USD of 301 USD.", McpHost.Text(after));
        Assert.Contains("Not paid yet: 5B", McpHost.Text(after));

        var remind = await host.CallToolAsync(admin.AccessToken, "send_payment_reminder");
        Assert.Equal("Sent a payment reminder for the Oct 1, 2026 receipt to the 1 apartment that hasn't paid.", McpHost.Text(remind));
        Assert.Contains(host.Api.Push.Sent, p => p.Title == "Payment reminder" && p.Tokens.Contains("ExponentPushToken[neighbour-5b-token]") && !p.Tokens.Contains("ExponentPushToken[resident-3a-token]"));

        var again = await host.CallToolAsync(admin.AccessToken, "send_payment_reminder");
        Assert.True(McpHost.IsError(again));
        Assert.Contains("already sent a reminder", McpHost.Text(again));
    }

    [Fact]
    public async Task Issuing_a_receipt_previews_first_and_only_confirm_issues_it()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var admin = await host.ConnectAsync(McpHost.AdminPhone);
        var before = await host.Api.Db(db => db.Receipts.CountAsync());

        var format = await host.CallToolAsync(admin.AccessToken, "get_receipt_format");
        Assert.Equal("Receipts are in USD. Amount fields: Maintenance, Electricity. Text fields: Note.", McpHost.Text(format));

        var args = new { amounts = new { maintenance = 25, Electricity = 40 }, notes = new { Note = "November" }, date = "2026-11-01", apartments = new[] { "5B" } };
        var preview = await host.CallToolAsync(admin.AccessToken, "issue_receipt", args);
        Assert.StartsWith("Preview, nothing has been issued yet. A receipt of 65 USD dated Nov 1, 2026 (Maintenance 25, Electricity 40, Note: November) for apartments 5B", McpHost.Text(preview));
        Assert.Equal(before, await host.Api.Db(db => db.Receipts.CountAsync()));

        var issued = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { args.amounts, args.notes, args.date, args.apartments, confirm = true });
        Assert.Equal("Receipt added successfully. 65 USD dated Nov 1, 2026 was issued to apartments 5B, and their residents were notified.", McpHost.Text(issued));
        Assert.Equal(before + 1, await host.Api.Db(db => db.Receipts.CountAsync()));
        var receipt = await host.Api.Db(db => db.Receipts.Include(r => r.Assignments).OrderByDescending(r => r.Id).FirstAsync());
        Assert.Equal(65m, receipt.Total);
        Assert.Equal(new DateOnly(2026, 11, 1), receipt.Date);
        Assert.Single(receipt.Assignments);
        Assert.Contains(host.Api.Push.Sent, p => p.Title == "New receipt" && p.Tokens.Contains("ExponentPushToken[neighbour-5b-token]"));

        var neighbour = await host.ConnectAsync(McpHost.NeighbourPhone);
        Assert.StartsWith("You owe 215.50 USD across 2 unpaid receipts", McpHost.Text(await host.CallToolAsync(neighbour.AccessToken, "get_my_balance")));
    }

    [Fact]
    public async Task Issue_receipt_explains_bad_input_instead_of_guessing()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var admin = await host.ConnectAsync(McpHost.AdminPhone);

        var unknownField = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { amounts = new { Water = 10 } });
        Assert.True(McpHost.IsError(unknownField));
        Assert.Equal("\"Water\" is not an amount field. Amount fields: Maintenance, Electricity.", McpHost.Text(unknownField));

        var zero = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { amounts = new { Maintenance = 0 }, confirm = true });
        Assert.Equal("Enter at least one amount greater than 0.", McpHost.Text(zero));

        var cents = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { amounts = new { Maintenance = 10.123 } });
        Assert.True(McpHost.IsError(cents));

        var stranger = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { amounts = new { Maintenance = 10 }, apartments = new[] { "7C" } });
        Assert.Equal("Not approved apartments of the building: 7C.", McpHost.Text(stranger));
    }

    [Fact]
    public async Task A_usd_and_an_lbp_receipt_are_issued_together_after_one_preview()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var admin = await host.ConnectAsync(McpHost.AdminPhone);
        var before = await host.Api.Db(db => db.Receipts.CountAsync());

        var receipts = new object[]
        {
            new { currency = "USD", amounts = new { Maintenance = 25, Electricity = 40 }, date = "2026-11-01" },
            new { currency = "LBP", amounts = new { Maintenance = 2225000 }, apartments = new[] { "5B" }, date = "2026-11-01" },
        };
        var preview = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { receipts });
        Assert.StartsWith("Preview, nothing has been issued yet. 2 separate receipts: " +
            "(1) 65 USD dated Nov 1, 2026 (Maintenance 25, Electricity 40) for all 2 approved apartments; " +
            "(2) 2,225,000 LBP dated Nov 1, 2026 (Maintenance 2,225,000) for apartments 5B.", McpHost.Text(preview));
        Assert.Equal(before, await host.Api.Db(db => db.Receipts.CountAsync()));

        var issued = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { receipts, confirm = true });
        Assert.StartsWith("2 receipts were issued: 65 USD dated Nov 1, 2026 to all 2 approved apartments; 2,225,000 LBP dated Nov 1, 2026 to apartments 5B.", McpHost.Text(issued));
        Assert.Equal(before + 2, await host.Api.Db(db => db.Receipts.CountAsync()));

        var made = await host.Api.Db(db => db.Receipts.Include(r => r.Assignments).OrderByDescending(r => r.Id).Take(2).ToListAsync());
        var lbp = made.Single(r => r.Currency == "LBP");
        var usd = made.Single(r => r.Currency == "USD");
        Assert.Equal(2225000m, lbp.Total);
        Assert.Single(lbp.Assignments);
        Assert.Equal(65m, usd.Total);
        Assert.Equal(2, usd.Assignments.Count);

        // The neighbour in 5B is billed both, and the two currencies stay apart.
        var neighbour = await host.ConnectAsync(McpHost.NeighbourPhone);
        Assert.StartsWith("You owe 215.50 USD and 2,225,000 LBP across 3 unpaid receipts", McpHost.Text(await host.CallToolAsync(neighbour.AccessToken, "get_my_balance")));
    }

    [Fact]
    public async Task Several_receipts_are_all_checked_before_any_is_issued()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var admin = await host.ConnectAsync(McpHost.AdminPhone);
        var before = await host.Api.Db(db => db.Receipts.CountAsync());

        var badSecond = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new
        {
            receipts = new object[] { new { amounts = new { Maintenance = 10 } }, new { currency = "EUR", amounts = new { Maintenance = 10 } } },
            confirm = true,
        });
        Assert.True(McpHost.IsError(badSecond));
        Assert.Equal("Receipt 2: currency must be USD or LBP.", McpHost.Text(badSecond));
        Assert.Equal(before, await host.Api.Db(db => db.Receipts.CountAsync()));

        var unknownLine = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new
        {
            receipts = new object[] { new { amounts = new { Maintenance = 10 } }, new { currency = "LBP", amounts = new { Water = 10 } } },
            confirm = true,
        });
        Assert.StartsWith("Receipt 2: \"Water\" is not an amount field.", McpHost.Text(unknownLine));
        Assert.Equal(before, await host.Api.Db(db => db.Receipts.CountAsync()));

        var both = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { amounts = new { Maintenance = 10 }, receipts = new object[] { new { amounts = new { Maintenance = 10 } } } });
        Assert.Contains("not both", McpHost.Text(both));

        var empty = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { receipts = Array.Empty<object>() });
        Assert.Equal("receipts needs at least one receipt.", McpHost.Text(empty));

        var tooMany = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { receipts = Enumerable.Range(0, 6).Select(_ => (object)new { amounts = new { Maintenance = 1 } }).ToArray() });
        Assert.Equal("Issue at most 5 receipts at once.", McpHost.Text(tooMany));
        Assert.Equal(before, await host.Api.Db(db => db.Receipts.CountAsync()));
    }

    [Fact]
    public async Task A_single_receipt_can_name_its_own_currency()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var admin = await host.ConnectAsync(McpHost.AdminPhone);

        // The building's format says USD; this receipt is in lira.
        var preview = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { currency = "lbp", amounts = new { Maintenance = 4450000 }, apartments = new[] { "5B" } });
        Assert.StartsWith("Preview, nothing has been issued yet. A receipt of 4,450,000 LBP dated today (Maintenance 4,450,000) for apartments 5B", McpHost.Text(preview));
        var issued = await host.CallToolAsync(admin.AccessToken, "issue_receipt", new { currency = "LBP", amounts = new { Maintenance = 4450000 }, apartments = new[] { "5B" }, confirm = true });
        Assert.StartsWith("Receipt added successfully. 4,450,000 LBP dated today was issued to apartments 5B", McpHost.Text(issued));
        var receipt = await host.Api.Db(db => db.Receipts.OrderByDescending(r => r.Id).FirstAsync());
        Assert.Equal("LBP", receipt.Currency);
    }

    [Fact]
    public async Task An_admin_handles_join_requests()
    {
        using var host = new McpHost();
        var (admin, _, _) = await host.SeedBuildingAsync();
        var buildingId = (await host.Api.Call<Baytak.Api.Functions.BuildingFunctions>((f, r, ct) => f.List(r, ct), token: admin)).Json[0].GetProperty("value").GetInt32();
        var newcomer = await host.Api.LoginAsync("79 555 555");
        Assert.True((await host.Api.Call<Baytak.Api.Functions.ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct),
            new { appartmentNumber = "7C", appartmentDescription = "Corner", floor = 7, buildingId }, newcomer)).Ok);

        var connection = await host.ConnectAsync(McpHost.AdminPhone);
        var requests = await host.CallToolAsync(connection.AccessToken, "list_join_requests");
        Assert.StartsWith("Request ", McpHost.Text(requests));
        Assert.Contains("apartment 7C, 7th floor (Corner)", McpHost.Text(requests));
        var id = requests.GetProperty("structuredContent").GetProperty("requests")[0].GetProperty("id").GetInt32();

        var approved = await host.CallToolAsync(connection.AccessToken, "approve_join_request", new { request_id = id });
        Assert.Equal("Apartment has been added to the building successfully", McpHost.Text(approved));
        Assert.Equal("No join requests are waiting.", McpHost.Text(await host.CallToolAsync(connection.AccessToken, "list_join_requests")));
        Assert.StartsWith("3 apartments:", McpHost.Text(await host.CallToolAsync(connection.AccessToken, "list_apartments")));
    }

    [Fact]
    public async Task Requests_from_unknown_browser_origins_are_refused()
    {
        using var host = new McpHost();
        var connection = await host.ConnectAsync(McpHost.ResidentPhone);
        var evil = await host.Mcp(connection.AccessToken, new { jsonrpc = "2.0", id = 1, method = "ping" }, origin: "https://evil.example");
        Assert.Equal(403, evil.Status);
        var chatgpt = await host.Mcp(connection.AccessToken, new { jsonrpc = "2.0", id = 1, method = "ping" }, origin: "https://chatgpt.com");
        Assert.Equal(200, chatgpt.Status);
    }

    private static List<string> Names(JsonElement list) =>
        list.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
}
