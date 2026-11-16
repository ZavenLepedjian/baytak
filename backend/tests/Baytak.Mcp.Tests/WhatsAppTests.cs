using System.Text.Json;
using Baytak.Api.Data;
using Baytak.Api.Functions;
using Baytak.Api.Tests;
using Baytak.Mcp.Security;
using Baytak.Mcp.WhatsApp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Baytak.Mcp.Tests;

/// <summary>
/// The WhatsApp assistant against a seeded building. The language model and WhatsApp itself are played by the test;
/// the tools, the API and its rules are the real ones.
/// </summary>
public class WhatsAppTests
{
    private static readonly string[] ResidentTools = ["get_my_balance", "list_my_receipts", "get_my_apartment"];

    // ==================================================================== the webhook

    [Fact]
    public async Task Meta_can_verify_the_webhook_only_with_our_token()
    {
        using var host = new McpHost();

        var ok = await host.WhatsAppFunction((f, r) => Task.FromResult(f.Verify(r)),
            r => r.QueryString = new QueryString($"?hub.mode=subscribe&hub.verify_token={McpHost.WhatsAppVerifyToken}&hub.challenge=1158201444"));
        var okResult = Assert.IsType<ContentResult>(ok);
        Assert.Equal(200, okResult.StatusCode);
        Assert.Equal("1158201444", okResult.Content);

        var wrong = await host.WhatsAppFunction((f, r) => Task.FromResult(f.Verify(r)),
            r => r.QueryString = new QueryString("?hub.mode=subscribe&hub.verify_token=guess&hub.challenge=1"));
        Assert.Equal(403, Assert.IsType<ContentResult>(wrong).StatusCode);
    }

    [Fact]
    public async Task The_webhook_is_closed_until_whatsapp_is_configured()
    {
        using var host = new McpHost(o => o.OpenAiKey = "");

        var verify = await host.WhatsAppFunction((f, r) => Task.FromResult(f.Verify(r)),
            r => r.QueryString = new QueryString($"?hub.mode=subscribe&hub.verify_token={McpHost.WhatsAppVerifyToken}&hub.challenge=1"));
        Assert.Equal(404, Assert.IsType<ContentResult>(verify).StatusCode);

        var post = await host.Webhook(host.WebhookBody(McpHost.ResidentPhone, new { type = "text", text = new { body = "hi" } }));
        Assert.Equal(404, Assert.IsType<ContentResult>(post.Result).StatusCode);
        Assert.Empty(post.Messages);
    }

    [Fact]
    public async Task Only_bodies_signed_by_meta_are_accepted_and_each_message_is_queued_once()
    {
        using var host = new McpHost();
        var body = host.WebhookBody(McpHost.ResidentPhone, new { type = "text", text = new { body = "Do I owe anything?" } });

        var unsigned = await host.Webhook(body, secret: null);
        Assert.Equal(401, Assert.IsType<ContentResult>(unsigned.Result).StatusCode);
        Assert.Empty(unsigned.Messages);

        var forged = await host.Webhook(body, secret: "someone-elses-secret");
        Assert.Equal(401, Assert.IsType<ContentResult>(forged.Result).StatusCode);
        Assert.Empty(forged.Messages);

        var first = await host.Webhook(body);
        Assert.Equal(200, Assert.IsType<StatusCodeResult>(first.Result).StatusCode);
        var queued = Assert.Single(first.Messages);
        Assert.DoesNotContain(McpHost.WaId(McpHost.ResidentPhone), queued); // sealed: no phone number or text on the queue
        Assert.DoesNotContain("owe", queued);

        // Meta delivers at least once: the same message again is accepted and not queued again.
        var again = await host.Webhook(body);
        Assert.Equal(200, Assert.IsType<StatusCodeResult>(again.Result).StatusCode);
        Assert.Empty(again.Messages);
    }

    [Fact]
    public async Task Receipts_other_numbers_and_stale_messages_are_not_queued()
    {
        using var host = new McpHost();

        var otherNumber = await host.Webhook(host.WebhookBody(McpHost.ResidentPhone, new { type = "text", text = new { body = "hi" } }, numberId: "999"));
        Assert.Empty(otherNumber.Messages);

        var stale = await host.Webhook(host.WebhookBody(McpHost.ResidentPhone, new { type = "text", text = new { body = "hi" } }, sentAt: host.Api.Time.GetUtcNow().AddHours(-2)));
        Assert.Empty(stale.Messages);

        var statuses = await host.Webhook("""{"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{"messaging_product":"whatsapp","metadata":{"phone_number_id":"106540352242922"},"statuses":[{"id":"wamid.x","status":"delivered"}]}}]}]}""");
        Assert.Equal(200, Assert.IsType<StatusCodeResult>(statuses.Result).StatusCode);
        Assert.Empty(statuses.Messages);

        var garbage = await host.Webhook("""{"entry":"nope"}""");
        Assert.Equal(200, Assert.IsType<StatusCodeResult>(garbage.Result).StatusCode);
        Assert.Empty(garbage.Messages);
    }

    [Fact]
    public async Task A_queued_message_is_answered_end_to_end()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        host.Model.Asks("get_my_balance");
        host.Model.Says("You owe 150.50 USD for the Oct 1 receipt.");

        var webhook = await host.Webhook(host.WebhookBody(McpHost.ResidentPhone, new { type = "text", text = new { body = "Do I owe anything?" } }, id: "wamid.first"));
        await host.WhatsAppFunction<object?>(async (f, _) =>
        {
            await f.Process(Assert.Single(webhook.Messages), CancellationToken.None);
            return null;
        });

        Assert.Equal("You owe 150.50 USD for the Oct 1 receipt.", host.WhatsApp.Last.Text);
        Assert.Equal(McpHost.WaId(McpHost.ResidentPhone), host.WhatsApp.Last.To);
        Assert.Equal(["wamid.first"], host.WhatsApp.Read); // marked read, which also shows "typing"

        // A queue message that is not ours is dropped, not thrown.
        await host.WhatsAppFunction<object?>(async (f, _) =>
        {
            await f.Process("not-a-sealed-message", CancellationToken.None);
            return null;
        });
        Assert.Single(host.WhatsApp.Sent);
    }

    // ==================================================================== who is writing

    [Fact]
    public async Task A_resident_is_signed_in_by_their_number_and_gets_the_resident_tools_only()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var smsBefore = host.Api.Sms.Sent.Count;
        host.Model.Asks("get_my_balance");
        host.Model.Says("You owe 150.50 USD.");

        await host.Say(McpHost.ResidentPhone, "Do I owe anything?");

        Assert.Equal(smsBefore, host.Api.Sms.Sent.Count); // no code is sent: WhatsApp already proved the number
        Assert.Equal(2, host.Model.Calls.Count);
        Assert.Equal(ResidentTools.Order(), host.Model.Calls[0].Tools.Order());
        Assert.Contains("a resident", host.Model.System);
        Assert.Contains("You owe 150.50 USD across 1 unpaid receipt", host.Model.LastShown); // the tool's real answer went to the model
        Assert.Equal("You owe 150.50 USD.", host.WhatsApp.Last.Text);
        Assert.Empty(host.WhatsApp.Last.Buttons);
    }

    [Fact]
    public async Task An_administrator_gets_all_twelve_tools()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        host.Model.Asks("get_collection_status");
        host.Model.Says("Nobody has paid yet.");

        await host.Say(McpHost.AdminPhone, "who paid?");

        Assert.Equal(12, host.Model.Calls[0].Tools.Count);
        Assert.Contains("an approved building administrator", host.Model.System);
        Assert.Contains("0 of 2 apartments have paid", host.Model.LastShown);
    }

    [Fact]
    public async Task A_number_without_an_account_is_told_how_to_start_once_a_day_and_never_reaches_the_model()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var users = await host.Api.Db(db => db.Users.CountAsync());

        await host.Say("76 999 000", "hello");
        await host.Say("76 999 000", "anyone?");

        var only = Assert.Single(host.WhatsApp.Sent);
        Assert.Contains("isn't linked to a Baytak account", only.Text);
        Assert.Contains("https://", only.Text);
        Assert.Empty(host.Model.Calls);
        Assert.Equal(users, await host.Api.Db(db => db.Users.CountAsync())); // writing to the assistant never creates an account

        host.Api.Time.Advance(TimeSpan.FromHours(25));
        await host.Say("76 999 000", "مرحبا");
        Assert.Equal(2, host.WhatsApp.Sent.Count);
        Assert.Contains("غير مرتبط بحساب", host.WhatsApp.Last.Text);
    }

    [Fact]
    public async Task A_resident_cannot_reach_administrator_tools_even_if_the_model_tries()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var before = await host.Api.Db(db => db.Receipts.CountAsync());
        host.Model.Asks("issue_receipt", new { amounts = new { Maintenance = 5 }, confirm = true });
        host.Model.Says("Only your building administrator can issue receipts.");

        await host.Say(McpHost.ResidentPhone, "issue a receipt of 5 dollars to everyone");

        Assert.Equal("Error: Administrator access is required.", host.Model.LastShown);
        Assert.Equal(before, await host.Api.Db(db => db.Receipts.CountAsync()));
        Assert.Empty(host.WhatsApp.Last.Buttons);
    }

    // ==================================================================== changes need the person's Confirm

    [Fact]
    public async Task Marking_paid_changes_nothing_until_the_administrator_taps_confirm()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        host.Model.Asks("set_payment_status", new { apartments = new[] { "3a" }, status = "paid" });

        await host.Say(McpHost.AdminPhone, "3A paid");

        var ask = host.WhatsApp.Last;
        Assert.Equal("Mark apartment 3A as paid on the Oct 1, 2026 receipt (150.50 USD per apartment)?", ask.Text);
        Assert.Equal(["Confirm", "Cancel"], ask.Buttons.Select(b => b.Title));
        Assert.Equal(0, await host.Api.Db(db => db.ReceiptAssignments.CountAsync(a => a.Status == PaymentStatuses.Paid)));
        Assert.Single(host.Model.Calls); // the model's turn ended: it is not asked whether to go ahead

        await host.Tap(McpHost.AdminPhone, ask.Buttons[0].Id);

        Assert.Equal("Marked 3A as paid on the Oct 1, 2026 receipt.", host.WhatsApp.Last.Text);
        Assert.Equal(1, await host.Api.Db(db => db.ReceiptAssignments.CountAsync(a => a.Status == PaymentStatuses.Paid)));
        Assert.Single(host.Model.Calls); // carrying it out needs no model either

        // The same button again: nothing more happens.
        await host.Tap(McpHost.AdminPhone, ask.Buttons[0].Id);
        Assert.Contains("expired", host.WhatsApp.Last.Text);
        Assert.Equal(1, await host.Api.Db(db => db.ReceiptAssignments.CountAsync(a => a.Status == PaymentStatuses.Paid)));
    }

    [Fact]
    public async Task Cancel_changes_nothing_and_a_typed_yes_or_no_answers_the_buttons()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();

        host.Model.Asks("send_payment_reminder");
        await host.Say(McpHost.AdminPhone, "remind the ones who didn't pay");
        Assert.Equal("Send a payment reminder for the Oct 1, 2026 receipt (150.50 USD per apartment) to the 2 apartments that haven't paid?", host.WhatsApp.Last.Text);
        await host.Tap(McpHost.AdminPhone, host.WhatsApp.Last.Buttons[1].Id);
        Assert.Equal("Cancelled. Nothing was changed.", host.WhatsApp.Last.Text);
        Assert.DoesNotContain(host.Api.Push.Sent, p => p.Title == "Payment reminder");

        host.Model.Asks("send_payment_reminder");
        await host.Say(McpHost.AdminPhone, "ok remind them");
        await host.Say(McpHost.AdminPhone, "No");
        Assert.Equal("Cancelled. Nothing was changed.", host.WhatsApp.Last.Text);
        Assert.DoesNotContain(host.Api.Push.Sent, p => p.Title == "Payment reminder");

        host.Model.Asks("send_payment_reminder");
        await host.Say(McpHost.AdminPhone, "remind them please");
        await host.Say(McpHost.AdminPhone, "Yes!");
        Assert.Equal("Sent a payment reminder for the Oct 1, 2026 receipt to the 2 apartments that haven't paid.", host.WhatsApp.Last.Text);
        Assert.Contains(host.Api.Push.Sent, p => p.Title == "Payment reminder");
        Assert.Equal(3, host.Model.Calls.Count); // "No" and "Yes!" never went to the model
    }

    [Fact]
    public async Task Moving_on_drops_the_waiting_change_and_its_buttons_stop_working()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        host.Model.Asks("set_payment_status", new { apartments = new[] { "5B" }, status = "paid" });
        await host.Say(McpHost.AdminPhone, "5B paid");
        var ask = host.WhatsApp.Last;

        host.Model.Says("Two apartments are in the building.");
        await host.Say(McpHost.AdminPhone, "wait, how many apartments do we have?");
        Assert.Equal("Two apartments are in the building.", host.WhatsApp.Last.Text);
        // The model was told that nothing happened, so it cannot claim otherwise.
        Assert.Contains(host.Model.Calls[^1].Messages.EnumerateArray(), m => m.GetProperty("content").GetString()!.Contains("did not confirm"));

        await host.Tap(McpHost.AdminPhone, ask.Buttons[0].Id);
        Assert.Contains("expired", host.WhatsApp.Last.Text);
        Assert.Equal(0, await host.Api.Db(db => db.ReceiptAssignments.CountAsync(a => a.Status == PaymentStatuses.Paid)));
    }

    [Fact]
    public async Task A_confirm_button_expires()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        host.Model.Asks("set_payment_status", new { apartments = new[] { "5B" }, status = "paid" });
        await host.Say(McpHost.AdminPhone, "5B paid");
        var ask = host.WhatsApp.Last;

        host.Api.Time.Advance(TimeSpan.FromMinutes(11));
        await host.Tap(McpHost.AdminPhone, ask.Buttons[0].Id);

        Assert.Contains("expired", host.WhatsApp.Last.Text);
        Assert.Equal(0, await host.Api.Db(db => db.ReceiptAssignments.CountAsync(a => a.Status == PaymentStatuses.Paid)));
    }

    [Fact]
    public async Task Confirm_acts_on_the_receipt_that_was_shown_even_if_a_newer_one_arrived()
    {
        using var host = new McpHost();
        var (admin, _, _) = await host.SeedBuildingAsync();
        var shown = await host.Api.Db(db => db.Receipts.Select(r => r.Id).SingleAsync());
        host.Model.Asks("set_payment_status", new { apartments = new[] { "3A" }, status = "paid" }); // no receipt_id: "the newest"
        await host.Say(McpHost.AdminPhone, "3A paid");
        var ask = host.WhatsApp.Last;

        // Meanwhile a new receipt is issued from the app, so "the newest" is now a different receipt.
        Assert.True((await host.Api.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), token: admin, rawBody: """
            {"currency":"USD","appartmentIds":"","version":"v2.0",
             "receipt":{"currency":"USD","date":"2026-10-14","customFields":[{"fieldName":"Maintenance","type":"Amount","value":"20"}]}}
            """)).Ok);

        await host.Tap(McpHost.AdminPhone, ask.Buttons[0].Id);

        var paid = await host.Api.Db(db => db.ReceiptAssignments.Where(a => a.Status == PaymentStatuses.Paid).Select(a => a.ReceiptId).ToListAsync());
        Assert.Equal([shown], paid);
    }

    [Fact]
    public async Task A_change_that_cannot_go_ahead_goes_back_to_the_model_instead_of_the_person()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        host.Model.Asks("set_payment_status", new { apartments = new[] { "9Z" }, status = "paid" });
        host.Model.Says("There is no apartment 9Z on that receipt. It has 3A and 5B.");

        await host.Say(McpHost.AdminPhone, "9Z paid");

        Assert.StartsWith("Error: Not on this receipt: 9Z.", host.Model.LastShown);
        Assert.Equal("There is no apartment 9Z on that receipt. It has 3A and 5B.", host.WhatsApp.Last.Text);
        Assert.Empty(host.WhatsApp.Last.Buttons);
    }

    [Fact]
    public async Task Issuing_a_receipt_shows_the_exact_receipt_in_arabic_and_issues_it_on_confirm()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var before = await host.Api.Db(db => db.Receipts.CountAsync());
        host.Model.Asks("get_receipt_format");
        // confirm=true from the model is not obeyed: the person decides.
        host.Model.Asks("issue_receipt", new { amounts = new { Maintenance = 25 }, notes = new { Note = "November" }, date = "2026-11-01", confirm = true });

        await host.Say(McpHost.AdminPhone, "أصدر إيصال صيانة ٢٥ دولار لكل الشقق");

        var ask = host.WhatsApp.Last;
        Assert.StartsWith("إصدار هذا الإيصال؟", ask.Text);
        Assert.Contains("25 USD", ask.Text);
        Assert.Contains("2026-11-01", ask.Text);
        Assert.Contains("Maintenance 25, Note: November", ask.Text);
        Assert.Contains("كل الشقق المعتمدة (2)", ask.Text);
        Assert.Equal(["تأكيد", "إلغاء"], ask.Buttons.Select(b => b.Title));
        Assert.Equal(before, await host.Api.Db(db => db.Receipts.CountAsync()));

        await host.Tap(McpHost.AdminPhone, ask.Buttons[0].Id);

        Assert.Equal(before + 1, await host.Api.Db(db => db.Receipts.CountAsync()));
        var receipt = await host.Api.Db(db => db.Receipts.Include(r => r.Assignments).OrderByDescending(r => r.Id).FirstAsync());
        Assert.Equal(25m, receipt.Total);
        Assert.Equal(new DateOnly(2026, 11, 1), receipt.Date);
        Assert.Equal(2, receipt.Assignments.Count);
        Assert.StartsWith("تم إصدار الإيصال", host.WhatsApp.Last.Text);
        Assert.Contains("25 USD", host.WhatsApp.Last.Text);
    }

    [Fact]
    public async Task Two_receipts_in_two_currencies_are_shown_separately_and_issued_together()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var before = await host.Api.Db(db => db.Receipts.CountAsync());
        host.Model.Asks("issue_receipt", new
        {
            receipts = new object[]
            {
                new { currency = "USD", amounts = new { Maintenance = 30 }, apartments = new[] { "3A" } },
                new { currency = "LBP", amounts = new { Electricity = 890000 } },
            },
        });

        await host.Say(McpHost.AdminPhone, "bill 3A 30 dollars maintenance and everyone 890,000 lira electricity");

        var ask = host.WhatsApp.Last;
        Assert.StartsWith("Issue these 2 receipts?", ask.Text);
        Assert.Contains("1. 30 USD per apartment, today", ask.Text);
        Assert.Contains("For: apartment 3A", ask.Text);
        Assert.Contains("2. 890,000 LBP per apartment, today", ask.Text);
        Assert.Contains("For: all 2 approved apartments", ask.Text);

        await host.Tap(McpHost.AdminPhone, ask.Buttons[0].Id);
        Assert.Equal(before + 2, await host.Api.Db(db => db.Receipts.CountAsync()));
        Assert.StartsWith("2 receipts were issued", host.WhatsApp.Last.Text);
    }

    [Fact]
    public async Task Join_requests_are_approved_by_name_only_after_confirm()
    {
        using var host = new McpHost();
        var (admin, _, _) = await host.SeedBuildingAsync();
        var buildings = await host.Api.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: admin);
        var newcomer = await host.Api.LoginAsync("78 444 444");
        Assert.True((await host.Api.Call<UserFunctions>((f, r, ct) => f.SaveProfile(r, ct), new { firstName = "Rami", lastName = "Haddad" }, newcomer)).Ok);
        Assert.True((await host.Api.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct),
            new { appartmentNumber = "7C", appartmentDescription = "Top floor", floor = 7, buildingId = buildings.Json[0].GetProperty("value").GetInt32() }, newcomer)).Ok);
        var requestId = await host.Api.Db(db => db.Apartments.Where(a => a.ApartmentNumber == "7C").Select(a => a.Id).SingleAsync());

        host.Model.Asks("approve_join_request", new { request_id = requestId });
        await host.Say(McpHost.AdminPhone, "approve Rami");

        Assert.Equal("Approve the join request for apartment 7C, 7th floor, from Rami Haddad?", host.WhatsApp.Last.Text);
        Assert.Equal(ApartmentStatuses.Pending, await host.Api.Db(db => db.Apartments.Where(a => a.Id == requestId).Select(a => a.Status).SingleAsync()));

        await host.Tap(McpHost.AdminPhone, host.WhatsApp.Last.Buttons[0].Id);
        Assert.Equal(ApartmentStatuses.Approved, await host.Api.Db(db => db.Apartments.Where(a => a.Id == requestId).Select(a => a.Status).SingleAsync()));

        // A request that is not waiting cannot even be offered for confirmation.
        host.Model.Asks("reject_join_request", new { request_id = 99999 });
        host.Model.Says("I can't find that request.");
        await host.Say(McpHost.AdminPhone, "reject request 99999");
        Assert.StartsWith("Error: No waiting join request has the id 99999.", host.Model.LastShown);
        Assert.Empty(host.WhatsApp.Last.Buttons);
    }

    // ==================================================================== memory, limits, failures

    [Fact]
    public async Task The_conversation_is_remembered_for_a_while_then_forgotten()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        host.Model.Says("Hello! Ask me what you owe.");
        await host.Say(McpHost.ResidentPhone, "hi");

        host.Model.Says("Yes.");
        await host.Say(McpHost.ResidentPhone, "can you repeat that?");
        var second = host.Model.Calls[^1].Messages.EnumerateArray().Select(m => m.GetProperty("content").GetString()).ToList();
        Assert.Equal(4, second.Count); // instructions, "hi", the answer, the new message
        Assert.Equal("hi", second[1]);
        Assert.Equal("Hello! Ask me what you owe.", second[2]);

        host.Api.Time.Advance(TimeSpan.FromHours(3));
        host.Model.Says("Hello again.");
        await host.Say(McpHost.ResidentPhone, "hi");
        Assert.Equal(2, host.Model.Calls[^1].Messages.GetArrayLength()); // instructions and the new message only
    }

    [Fact]
    public async Task What_is_stored_is_keyed_by_a_fingerprint_and_old_conversations_are_deleted()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        host.Model.Says("Hello!");
        await host.Say(McpHost.ResidentPhone, "hi");

        var store = host.Services.GetRequiredService<MemoryChatStore>();
        var sealer = host.Services.GetRequiredService<Sealer>();
        var key = sealer.Fingerprint("whatsapp", McpHost.WaId(McpHost.ResidentPhone));
        Assert.DoesNotContain(McpHost.WaId(McpHost.ResidentPhone), key);
        Assert.Single((await store.LoadAsync(key, CancellationToken.None)).History, t => t.Role == "user" && t.Text == "hi");

        // The nightly clean-up keeps two days.
        async Task PurgeAsync() => await host.WhatsAppFunction<object?>(async (f, _) =>
        {
            await f.Purge(null!, CancellationToken.None);
            return null;
        });
        host.Api.Time.Advance(TimeSpan.FromHours(40));
        await PurgeAsync();
        Assert.Equal(1, store.Count);
        host.Api.Time.Advance(TimeSpan.FromHours(10));
        await PurgeAsync();
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_long_arabic_conversation_still_fits_in_one_stored_value()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();
        var sealer = host.Services.GetRequiredService<Sealer>();
        var store = host.Services.GetRequiredService<MemoryChatStore>();
        var key = sealer.Fingerprint("whatsapp", McpHost.WaId(McpHost.ResidentPhone));

        for (var i = 0; i < 12; i++)
        {
            host.Model.Says(new string('ب', 1400));
            await host.Say(McpHost.ResidentPhone, new string('س', 1400));
        }

        var state = await store.LoadAsync(key, CancellationToken.None);
        Assert.NotEmpty(state.History);
        Assert.Equal("user", state.History[0].Role);
        Assert.InRange(sealer.Seal(TableChatStore.Purpose, state).Length, 1, TableChatStore.MaxSealedLength);
    }

    [Fact]
    public async Task A_number_that_writes_too_much_is_told_once_and_then_ignored()
    {
        using var host = new McpHost(o => o.MessagesPerHour = 2);
        await host.SeedBuildingAsync();
        host.Model.Says("one");
        host.Model.Says("two");

        await host.Say(McpHost.ResidentPhone, "1");
        await host.Say(McpHost.ResidentPhone, "2");
        await host.Say(McpHost.ResidentPhone, "3");
        await host.Say(McpHost.ResidentPhone, "4");

        Assert.Equal(["one", "two", "I've had a lot of messages from this number. Please try again in a little while."], host.WhatsApp.Sent.Select(m => m.Text));
        Assert.Equal(2, host.Model.Calls.Count);

        host.Api.Time.Advance(TimeSpan.FromMinutes(61));
        host.Model.Says("back");
        await host.Say(McpHost.ResidentPhone, "5");
        Assert.Equal("back", host.WhatsApp.Last.Text);
    }

    [Fact]
    public async Task Photos_and_voice_notes_get_a_short_notice()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();

        var webhook = await host.Webhook(host.WebhookBody(McpHost.ResidentPhone, new { type = "audio", audio = new { id = "1" } }));
        await host.WhatsAppFunction<object?>(async (f, _) =>
        {
            await f.Process(Assert.Single(webhook.Messages), CancellationToken.None);
            return null;
        });

        Assert.Equal("I can only read written messages for now. Type what you need.", host.WhatsApp.Last.Text);
        Assert.Empty(host.Model.Calls);
    }

    [Fact]
    public async Task Failures_end_in_a_short_apology_never_in_silence()
    {
        using var host = new McpHost();
        await host.SeedBuildingAsync();

        host.Model.Down = true;
        await host.Say(McpHost.ResidentPhone, "hi");
        Assert.Equal("Something went wrong on my side. Please try again in a moment.", host.WhatsApp.Last.Text);
        host.Model.Down = false;

        host.Model.Refuses();
        await host.Say(McpHost.ResidentPhone, "something the content filter stops");
        Assert.Equal("I can't help with that. Ask me about your building's fees and receipts.", host.WhatsApp.Last.Text);

        host.ApiDown = true;
        await host.Say(McpHost.ResidentPhone, "مرحبا");
        Assert.Equal("تعذّر الوصول إلى بيتك الآن. الرجاء المحاولة بعد قليل.", host.WhatsApp.Last.Text);
        host.ApiDown = false;

        // A model that keeps asking for tools is stopped.
        for (var i = 0; i < 6; i++) host.Model.Asks("get_my_apartment");
        await host.Say(McpHost.ResidentPhone, "hi");
        Assert.Equal("Something went wrong on my side. Please try again in a moment.", host.WhatsApp.Last.Text);
    }

    // ==================================================================== small pieces

    [Theory]
    [InlineData("yes", true)]
    [InlineData("Yes!", true)]
    [InlineData("  OK ", true)]
    [InlineData("نعم", true)]
    [InlineData("أكيد", true)]
    [InlineData("تمام.", true)]
    [InlineData("no", false)]
    [InlineData("Cancel", false)]
    [InlineData("لا", false)]
    [InlineData("إلغاء", false)]
    [InlineData("yes but only 3A", null)]
    [InlineData("how much?", null)]
    [InlineData("", null)]
    public void Only_a_plain_yes_or_no_answers_a_confirmation(string text, bool? expected) =>
        Assert.Equal(expected, BotTexts.YesOrNo(text));

    [Fact]
    public void The_language_follows_the_script_and_keeps_the_last_one_for_numbers()
    {
        Assert.Equal(BotTexts.Arabic, BotTexts.Detect("كم المبلغ؟", BotTexts.English));
        Assert.Equal(BotTexts.English, BotTexts.Detect("kifak, shu el 7seb?", BotTexts.Arabic));
        Assert.Equal(BotTexts.Arabic, BotTexts.Detect("25", BotTexts.Arabic));
    }

    [Fact]
    public void A_long_answer_is_cut_into_messages_at_line_breaks()
    {
        var text = string.Join("\n", Enumerable.Range(1, 400).Select(i => $"Line {i}: apartment paid"));
        var parts = GraphWhatsAppSender.Parts(text, GraphWhatsAppSender.MaxText).ToList();

        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.InRange(p.Length, 1, GraphWhatsAppSender.MaxText));
        Assert.All(parts, p => Assert.StartsWith("Line ", p)); // never cut inside a line
        Assert.Equal(text, string.Join("\n", parts));
    }

    [Fact]
    public void The_openai_url_is_built_from_the_base_url()
    {
        Assert.Equal("https://api.openai.com/v1/responses", OpenAiResponsesModel.ResponsesUrl("https://api.openai.com/v1").ToString());
        Assert.Equal("https://api.openai.com/v1/responses", OpenAiResponsesModel.ResponsesUrl("https://api.openai.com/").ToString());
    }
}
