using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Baytak.Mcp.Api;
using Baytak.Mcp.Common;

namespace Baytak.Mcp.Mcp;

/// <summary>A tool's answer: text the assistant can relay, plus the same facts as structured data.</summary>
public sealed record ToolResult(string Text, object? Structured = null, bool IsError = false)
{
    public static ToolResult Fail(string message) => new(message, null, true);

    public JsonObject ToJson()
    {
        var result = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Text }) };
        if (Structured is not null) result["structuredContent"] = JsonSerializer.SerializeToNode(Structured, Http.Json);
        if (IsError) result["isError"] = true;
        return result;
    }
}

internal sealed record Tool(
    string Name, string Title, string Description, string InputSchema,
    bool ReadOnly, bool Destructive, bool Idempotent, bool OpenWorld, bool AdminOnly,
    Func<JsonElement, McpSession, Task<ToolResult>> Run);

/// <summary>
/// The Baytak tools. Residents get the read tools about their own apartment; approved administrators also get the building
/// tools. Every call goes through the API as the signed-in user, so the API's rules decide what is allowed, not this list.
/// Phone numbers are never returned: they stay in the app.
/// </summary>
public sealed class BaytakTools
{
    private const string NoParameters = """{"type":"object","properties":{},"additionalProperties":false}""";
    private const string ReceiptIdParameter = """
        {"type":"object","properties":{"receipt_id":{"type":"integer","description":"Receipt id from get_collection_status. Default: the newest receipt."}},"additionalProperties":false}
        """;
    private const string RequestIdParameter = """
        {"type":"object","properties":{"request_id":{"type":"integer","description":"Join request id from list_join_requests."}},"required":["request_id"],"additionalProperties":false}
        """;
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly BaytakApi _api;
    private readonly Dictionary<string, Tool> _tools;

    public BaytakTools(BaytakApi api)
    {
        _api = api;
        _tools = Define().ToDictionary(t => t.Name);
    }

    public bool Exists(string name) => _tools.ContainsKey(name);

    /// <summary>
    /// Every account sees every tool. ChatGPT keeps one tool list per app, taken from whichever account connected or refreshed
    /// last, so a list that depended on the role would hide the administrator tools from anyone for as long as it was stale.
    /// The Baytak API refuses administrator actions for residents ("Administrator access is required."), whatever is listed.
    /// </summary>
    public async Task<JsonArray> ListAsync(McpSession session)
    {
        await session.IsApprovedAdminAsync(_api); // not used for the list; it makes a signed-out session fail here, as before
        return new JsonArray(_tools.Values.Select(Describe).ToArray<JsonNode?>());
    }

    public Task<ToolResult> CallAsync(string name, JsonElement arguments, McpSession session) => _tools[name].Run(arguments, session);

    private static JsonObject Describe(Tool t) => new()
    {
        ["name"] = t.Name,
        ["title"] = t.Title,
        ["description"] = t.Description,
        ["inputSchema"] = JsonNode.Parse(t.InputSchema),
        ["annotations"] = new JsonObject
        {
            ["title"] = t.Title,
            ["readOnlyHint"] = t.ReadOnly,
            ["destructiveHint"] = t.Destructive,
            ["idempotentHint"] = t.Idempotent,
            ["openWorldHint"] = t.OpenWorld,
        },
    };

    private IEnumerable<Tool> Define()
    {
        // ---------------------------------------------------------------- residents
        yield return new Tool("get_my_balance", "What I owe",
            "What the signed-in resident owes now, per currency (USD and LBP are never added together), with each unpaid receipt " +
            "and its lines. Use it for questions like \"do I owe anything?\" or \"how much is due this month?\".",
            NoParameters, true, false, true, false, false, MyBalanceAsync);

        yield return new Tool("list_my_receipts", "My receipts",
            "The signed-in resident's receipts, newest first, with date, total, currency and whether each is paid.",
            """
            {"type":"object","properties":{
              "status":{"type":"string","enum":["all","unpaid","paid"],"description":"Which receipts to include. Default all."},
              "limit":{"type":"integer","minimum":1,"maximum":50,"description":"How many recent receipts. Default 12."}},
             "additionalProperties":false}
            """,
            true, false, true, false, false, MyReceiptsAsync);

        yield return new Tool("get_my_apartment", "My apartment",
            "The signed-in person's apartment and building, the status of their join request, and whether they are a resident or a building administrator.",
            NoParameters, true, false, true, false, false, MyApartmentAsync);

        // ---------------------------------------------------------------- building administrators
        yield return new Tool("get_collection_status", "Collection status",
            "For building administrators: who has paid a receipt and who hasn't, apartment by apartment, with the amount collected " +
            "and expected, whether a reminder can be sent, and the building's other recent receipts.",
            ReceiptIdParameter, true, false, true, false, true, CollectionStatusAsync);

        yield return new Tool("set_payment_status", "Mark paid or unpaid",
            "For building administrators: marks one or more apartments as paid (or back to unpaid) on a receipt. Residents see the change in the app.",
            """
            {"type":"object","properties":{
              "apartments":{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":100,"description":"Apartment numbers as on the door plates, e.g. [\"3B\",\"4A\"]."},
              "status":{"type":"string","enum":["paid","unpaid"]},
              "receipt_id":{"type":"integer","description":"Receipt id from get_collection_status. Default: the newest receipt."}},
             "required":["apartments","status"],"additionalProperties":false}
            """,
            false, true, true, false, true, SetPaymentStatusAsync);

        yield return new Tool("send_payment_reminder", "Send a payment reminder",
            "For building administrators: sends a push notification to every apartment that hasn't paid a receipt yet. " +
            "Possible once a day per receipt.",
            ReceiptIdParameter, false, true, false, false, true, SendReminderAsync);

        yield return new Tool("list_join_requests", "Join requests",
            "For building administrators: requests from residents waiting to join the building, with the apartment they asked for.",
            NoParameters, true, false, true, false, true, JoinRequestsAsync);

        yield return new Tool("approve_join_request", "Approve a join request",
            "For building administrators: approves a join request, so the resident becomes part of the building and receives its receipts.",
            RequestIdParameter, false, true, true, false, true, (a, s) => DecideAsync(a, s, "appartment/approve"));

        yield return new Tool("reject_join_request", "Reject a join request",
            "For building administrators: rejects a join request. The resident is told and can send a corrected request from the app.",
            RequestIdParameter, false, true, true, false, true, (a, s) => DecideAsync(a, s, "appartment/reject"));

        yield return new Tool("list_apartments", "Apartments",
            "For building administrators: the building's approved apartments with floor and resident name.",
            NoParameters, true, false, true, false, true, ApartmentsAsync);

        yield return new Tool("get_receipt_format", "Receipt format",
            "For building administrators: the currency and the fields (amounts and notes) of the building's receipts. Read it before issue_receipt.",
            NoParameters, true, false, true, false, true, ReceiptFormatAsync);

        yield return new Tool("issue_receipt", "Issue receipts",
            "For building administrators: issues one or more receipts to the whole building or to chosen apartments, and notifies every resident. " +
            "A receipt has one currency, USD or LBP, and its amounts are never added to another receipt's. To bill both currencies, send two " +
            "receipts in one call (the receipts list). It cannot be edited or deleted afterwards. Call it first without confirm to get a preview, " +
            "show the preview to the administrator, and only after they clearly agree call it again with the same values and confirm=true. " +
            "Each call with confirm=true issues the receipts again, so never repeat it for receipts that were already issued.",
            """
            {"type":"object","properties":{
              "receipts":{"type":"array","minItems":1,"maxItems":5,"description":"One or more receipts to issue together, for example one in USD and one in LBP. Each is a separate receipt with its own currency and total. Use this OR the single-receipt fields below, not both.",
                "items":{"type":"object","properties":{
                  "currency":{"type":"string","enum":["USD","LBP"],"description":"Currency of this receipt. Default: the currency of the building's receipt format."},
                  "amounts":{"type":"object","additionalProperties":{"type":"number","minimum":0},"description":"Amount per Amount field of the receipt format, in this receipt's currency, e.g. {\"Maintenance\":25,\"Electricity\":40}. Fields left out are 0."},
                  "notes":{"type":"object","additionalProperties":{"type":"string"},"description":"Text per Text field of the format, e.g. {\"Note\":\"Pay by the 10th\"}."},
                  "date":{"type":"string","description":"Receipt date as YYYY-MM-DD. Default: today."},
                  "apartments":{"type":"array","items":{"type":"string"},"description":"Apartment numbers to bill. Default: every approved apartment."}},
                 "required":["amounts"],"additionalProperties":false}},
              "currency":{"type":"string","enum":["USD","LBP"],"description":"Single receipt: its currency. Default: the currency of the building's receipt format."},
              "amounts":{"type":"object","additionalProperties":{"type":"number","minimum":0},"description":"Single receipt: amount per Amount field of the receipt format, e.g. {\"Maintenance\":25,\"Electricity\":40}. Fields left out are 0."},
              "notes":{"type":"object","additionalProperties":{"type":"string"},"description":"Single receipt: text per Text field of the format, e.g. {\"Note\":\"Pay by the 10th\"}."},
              "date":{"type":"string","description":"Single receipt: date as YYYY-MM-DD. Default: today."},
              "apartments":{"type":"array","items":{"type":"string"},"description":"Single receipt: apartment numbers to bill. Default: every approved apartment."},
              "confirm":{"type":"boolean","description":"Leave false for a preview that changes nothing. true issues the receipts, only after the administrator agreed to the preview."}},
             "additionalProperties":false}
            """,
            false, true, false, false, true, IssueReceiptAsync);
    }

    // ==================================================================== residents

    private sealed record Line(string Name, decimal? Amount, string? Note);
    private sealed record MyReceipt(int ReceiptId, string? Date, decimal Total, string Currency, bool Paid, List<Line> Lines);
    private sealed record MyApartmentInfo(string Number, int Floor);

    private async Task<ToolResult> MyBalanceAsync(JsonElement args, McpSession s)
    {
        var (apartment, receipts, stop) = await LoadMyReceiptsAsync(s);
        if (stop is not null) return stop;

        var unpaid = receipts.Where(r => !r.Paid).ToList();
        var owed = unpaid.GroupBy(r => r.Currency).OrderBy(g => g.Key == "USD" ? 0 : 1)
            .Select(g => new { currency = g.Key, amount = g.Sum(r => r.Total) }).ToList();

        var text = owed.Count == 0
            ? receipts.Count > 0
                ? "You're all paid up. Nothing is due."
                : "No receipts yet. When your building administrator issues a receipt, it appears here and you get a notification."
            : $"You owe {string.Join(" and ", owed.Select(o => Money(o.amount, o.currency)))} across {unpaid.Count} unpaid {Plural(unpaid.Count, "receipt")}:\n" +
              string.Join("\n", unpaid.Select(Summary));

        return new ToolResult(text, new { apartment, owed, unpaidReceipts = unpaid.Select(ReceiptData) });
    }

    private async Task<ToolResult> MyReceiptsAsync(JsonElement args, McpSession s)
    {
        var filter = Str(args, "status") ?? "all";
        if (filter is not ("all" or "unpaid" or "paid")) return ToolResult.Fail("status must be all, unpaid or paid");
        var limit = Math.Clamp(Int(args, "limit") ?? 12, 1, 50);

        var (apartment, receipts, stop) = await LoadMyReceiptsAsync(s);
        if (stop is not null) return stop;

        var shown = receipts.Where(r => filter == "all" || (filter == "paid") == r.Paid).Take(limit).ToList();
        var text = shown.Count == 0
            ? filter == "all" ? "No receipts yet." : $"No {filter} receipts."
            : string.Join("\n", shown.Select(r => $"{Summary(r)} · {(r.Paid ? "Paid" : "Unpaid")}"));
        return new ToolResult(text, new { apartment, receipts = shown.Select(ReceiptData) });
    }

    private async Task<ToolResult> MyApartmentAsync(JsonElement args, McpSession s)
    {
        var status = await Get("user/status", s);
        if (!status.Ok) return ToolResult.Fail(status.Text);
        var role = status.Json().GetProperty("role").GetString();
        var userStatus = status.Json().GetProperty("userStatus").GetString();

        var apt = await Get("appartment/get", s);
        if (!apt.Ok) return ToolResult.Fail(apt.Text);
        var a = apt.Json();
        var number = a.GetProperty("appartmentNumber").GetString() ?? "";
        var building = a.GetProperty("buildingName").GetString() ?? "";
        var aptStatus = a.GetProperty("status").GetString() ?? "";
        var floor = a.GetProperty("floor").GetInt32();

        var account = role == "Admin"
            ? userStatus == "Approved" ? "You're an approved building administrator." : $"Your building administrator access is {userStatus?.ToLowerInvariant()}."
            : "You're signed in as a resident.";
        var home = number.Length == 0
            ? "You haven't joined a building yet. Open the Baytak app to find your building and register your apartment."
            : aptStatus switch
            {
                "Approved" => $"Apartment {number}, {OrdinalFloor(floor)}, {building}.",
                "Pending" => $"Your request for apartment {number} at {building} is waiting for the administrator's approval.",
                "Rejected" => $"Your request for apartment {number} at {building} was not approved. You can update it in the Baytak app.",
                _ => $"Apartment {number} at {building} ({aptStatus}).",
            };

        return new ToolResult($"{home} {account}", new
        {
            role = role == "Admin" ? "administrator" : "resident",
            administratorStatus = role == "Admin" ? userStatus : null,
            apartment = number.Length == 0 ? null : new { number, floor, building, status = aptStatus },
        });
    }

    /// <summary>The resident's receipts, or a ready answer when they have no approved apartment yet.</summary>
    private async Task<(MyApartmentInfo? Apartment, List<MyReceipt> Receipts, ToolResult? Stop)> LoadMyReceiptsAsync(McpSession s)
    {
        var reply = await Get("appartment/receipts", s);
        if (!reply.Ok) return (null, [], ToolResult.Fail(reply.Text));
        if (reply.IsMarker("Register Apaprtment")) return (null, [], await NotJoinedAsync(s));

        MyApartmentInfo? apartment = null;
        var receipts = new List<MyReceipt>();
        foreach (var item in reply.Json().EnumerateArray())
        {
            if (apartment is null && item.TryGetProperty("appartment", out var a) && a.ValueKind == JsonValueKind.Object)
                apartment = new MyApartmentInfo(a.GetProperty("appartmentNumber").GetString() ?? "", a.GetProperty("floor").GetInt32());
            if (!item.TryGetProperty("receipt", out var body) || body.ValueKind != JsonValueKind.String) continue;

            var (date, lines) = ParseReceiptBody(body.GetString());
            receipts.Add(new MyReceipt(
                item.GetProperty("receiptId").GetInt32(), date, item.GetProperty("totalPayment").GetDecimal(),
                item.GetProperty("currency").GetString() ?? "", item.GetProperty("status").GetString() == "Paid", lines));
        }
        return (apartment, receipts, null);
    }

    private async Task<ToolResult> NotJoinedAsync(McpSession s)
    {
        var reply = await Get("appartment/get", s);
        var a = reply.Ok ? reply.Json() : default;
        var status = reply.Ok ? a.GetProperty("status").GetString() : "";
        var building = reply.Ok ? a.GetProperty("buildingName").GetString() : "";
        var number = reply.Ok ? a.GetProperty("appartmentNumber").GetString() : "";
        var text = status switch
        {
            "Pending" => $"Your request to join {building} as apartment {number} is waiting for the administrator's approval. Receipts appear once it's approved.",
            "Rejected" => $"Your request to join {building} was not approved. You can update it in the Baytak app.",
            _ => "You haven't joined a building yet. Open the Baytak app to find your building and register your apartment.",
        };
        return new ToolResult(text, new { joined = false, requestStatus = string.IsNullOrEmpty(status) ? null : status });
    }

    // ==================================================================== administrators

    private async Task<ToolResult> CollectionStatusAsync(JsonElement args, McpSession s)
    {
        var (summary, stop) = await LoadSummaryAsync(Int(args, "receipt_id"), s);
        if (stop is not null) return stop;
        var sel = summary!.Value.GetProperty("selected");

        var currency = sel.GetProperty("currency").GetString()!;
        var count = sel.GetProperty("count").GetInt32();
        var paid = sel.GetProperty("paidCount").GetInt32();
        var apartments = sel.GetProperty("apartments").EnumerateArray().Select(a => new
        {
            apartmentId = a.GetProperty("apartmentId").GetInt32(),
            number = a.GetProperty("apartmentNumber").GetString(),
            floor = a.GetProperty("floor").GetInt32(),
            resident = NullIfEmpty(a.GetProperty("residentName").GetString()),
            status = a.GetProperty("status").GetString() == "Paid" ? "Paid" : "Unpaid",
        }).ToList();

        var headline = count == 0 ? "No apartments were billed" : paid == count ? "Everyone has paid" : $"{paid} of {count} apartments have paid";
        var unpaid = apartments.Where(a => a.status == "Unpaid").Select(a => a.resident is null ? a.number : $"{a.number} ({a.resident})").ToList();
        var canRemind = sel.GetProperty("canRemind").GetBoolean();
        var text = $"{FormatDate(sel.GetProperty("date").GetString())} receipt, {Money(sel.GetProperty("total").GetDecimal(), currency)} per apartment: {headline}. " +
                   $"Collected {Money(sel.GetProperty("collected").GetDecimal(), currency)} of {Money(sel.GetProperty("expected").GetDecimal(), currency)}." +
                   (unpaid.Count > 0 ? $"\nNot paid yet: {string.Join(", ", unpaid)}." : "") +
                   (unpaid.Count == 0 ? "" : canRemind ? "\nA payment reminder can be sent." : "\nA reminder was already sent for this receipt in the last 24 hours.");

        var others = summary.Value.GetProperty("receipts").EnumerateArray().Select(r => new
        {
            receiptId = r.GetProperty("id").GetInt32(),
            date = r.GetProperty("date").GetString(),
            total = r.GetProperty("total").GetDecimal(),
            currency = r.GetProperty("currency").GetString(),
            paidCount = r.GetProperty("paidCount").GetInt32(),
            count = r.GetProperty("count").GetInt32(),
        });

        return new ToolResult(text, new
        {
            receipt = new { receiptId = sel.GetProperty("id").GetInt32(), date = sel.GetProperty("date").GetString(), total = sel.GetProperty("total").GetDecimal(), currency },
            paidCount = paid,
            count,
            collected = sel.GetProperty("collected").GetDecimal(),
            expected = sel.GetProperty("expected").GetDecimal(),
            canRemind,
            apartments,
            recentReceipts = others,
        });
    }

    private async Task<ToolResult> SetPaymentStatusAsync(JsonElement args, McpSession s)
    {
        var numbers = StrArray(args, "apartments");
        if (numbers.Count == 0) return ToolResult.Fail("Name at least one apartment, for example [\"3B\"].");
        var status = Str(args, "status");
        if (status is not ("paid" or "unpaid")) return ToolResult.Fail("status must be paid or unpaid");

        var (summary, stop) = await LoadSummaryAsync(Int(args, "receipt_id"), s);
        if (stop is not null) return stop;
        var sel = summary!.Value.GetProperty("selected");
        var receiptId = sel.GetProperty("id").GetInt32();
        var onReceipt = sel.GetProperty("apartments").EnumerateArray()
            .ToDictionary(a => a.GetProperty("apartmentNumber").GetString()!.Trim(), a => a.GetProperty("apartmentId").GetInt32(), StringComparer.OrdinalIgnoreCase);

        var unknown = numbers.Where(n => !onReceipt.ContainsKey(n.Trim())).ToList();
        if (unknown.Count > 0)
            return ToolResult.Fail($"Not on this receipt: {string.Join(", ", unknown)}. Apartments on it: {string.Join(", ", onReceipt.Keys)}.");

        var canonical = onReceipt.Keys.ToDictionary(k => k, k => k, StringComparer.OrdinalIgnoreCase);
        var done = new List<string>();
        foreach (var number in numbers.Select(n => canonical[n.Trim()]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var reply = await Post("receipt/appartment/save-status",
                new { appartmentId = onReceipt[number], receiptId, status = status == "paid" ? "Paid" : "Pending" }, s);
            if (!reply.Ok) return ToolResult.Fail(done.Count == 0 ? reply.Text : $"Marked {Join(done)} first, then {number} failed: {reply.Text}");
            done.Add(number);
        }

        var date = FormatDate(sel.GetProperty("date").GetString());
        return new ToolResult($"Marked {Join(done)} as {(status == "paid" ? "paid" : "unpaid")} on the {date} receipt.",
            new { receiptId, apartments = done, status = status == "paid" ? "Paid" : "Unpaid" });
    }

    private async Task<ToolResult> SendReminderAsync(JsonElement args, McpSession s)
    {
        var (summary, stop) = await LoadSummaryAsync(Int(args, "receipt_id"), s);
        if (stop is not null) return stop;
        var sel = summary!.Value.GetProperty("selected");
        var receiptId = sel.GetProperty("id").GetInt32();

        var reply = await Post("collection/remind", new { receiptId }, s);
        if (!reply.Ok) return ToolResult.Fail(reply.Text);
        var sent = reply.Json().GetProperty("sent").GetInt32();
        return new ToolResult(
            $"Sent a payment reminder for the {FormatDate(sel.GetProperty("date").GetString())} receipt to the {sent} {Plural(sent, "apartment")} that {(sent == 1 ? "hasn't" : "haven't")} paid.",
            new { receiptId, reminded = sent });
    }

    private async Task<ToolResult> JoinRequestsAsync(JsonElement args, McpSession s)
    {
        var reply = await Get("appartment/pending/get", s);
        if (!reply.Ok) return ToolResult.Fail(reply.Text);
        if (reply.IsMarker("NoAppartments")) return new ToolResult("No join requests are waiting.", new { requests = Array.Empty<object>() });

        var requests = ApartmentRows(reply.Json()).ToList();
        var text = string.Join("\n", requests.Select(r =>
            $"Request {r.Id}: apartment {r.Number}, {OrdinalFloor(r.Floor)}{(r.Description is null ? "" : $" ({r.Description})")}" +
            (r.Resident is null ? "" : $", from {r.Resident}")));
        return new ToolResult(text, new { requests });
    }

    private async Task<ToolResult> DecideAsync(JsonElement args, McpSession s, string path)
    {
        if (Int(args, "request_id") is not { } id) return ToolResult.Fail("request_id is required. Get it from list_join_requests.");
        var reply = await Post(path, new { id }, s);
        return reply.Ok ? new ToolResult(reply.Text, new { requestId = id }) : ToolResult.Fail(reply.Text);
    }

    private async Task<ToolResult> ApartmentsAsync(JsonElement args, McpSession s)
    {
        var reply = await Get("appartment/approved/get", s);
        if (!reply.Ok) return ToolResult.Fail(reply.Text);
        if (reply.IsMarker("NoAppartments")) return new ToolResult("No apartments have joined the building yet.", new { apartments = Array.Empty<object>() });

        var apartments = ApartmentRows(reply.Json()).ToList();
        var text = $"{apartments.Count} {Plural(apartments.Count, "apartment")}:\n" + string.Join("\n", apartments.Select(a =>
            $"{a.Number}, {OrdinalFloor(a.Floor)}{(a.Resident is null ? "" : $": {a.Resident}")}"));
        return new ToolResult(text, new { apartments });
    }

    private sealed record FormatField(string Name, bool IsAmount);
    private sealed record ReceiptFormat(string Currency, List<FormatField> Fields);

    private async Task<ToolResult> ReceiptFormatAsync(JsonElement args, McpSession s)
    {
        var (format, stop) = await LoadFormatAsync(s);
        if (stop is not null) return stop;
        var amounts = format!.Fields.Where(f => f.IsAmount).Select(f => f.Name).ToList();
        var notes = format.Fields.Where(f => !f.IsAmount).Select(f => f.Name).ToList();
        return new ToolResult(
            $"Receipts are in {format.Currency}. Amount fields: {string.Join(", ", amounts)}." + (notes.Count > 0 ? $" Text fields: {string.Join(", ", notes)}." : ""),
            new { currency = format.Currency, amountFields = amounts, textFields = notes });
    }

    private const int MaxReceiptsPerCall = 5;

    /// <summary>One receipt, fully checked and ready to preview or issue.</summary>
    private sealed record ReceiptSpec(
        string Currency, Dictionary<string, string> Values, decimal Total, string? Date, List<string> Chosen, List<ApartmentRow> Targets, List<string> Lines);

    private async Task<ToolResult> IssueReceiptAsync(JsonElement args, McpSession s)
    {
        var (format, stop) = await LoadFormatAsync(s);
        if (stop is not null) return stop;

        // One receipt (the top-level fields) or several (the receipts list), never both.
        var items = new List<JsonElement>();
        var multiple = args.TryGetProperty("receipts", out var list) && list.ValueKind == JsonValueKind.Array;
        if (multiple)
        {
            if (args.TryGetProperty("amounts", out _)) return ToolResult.Fail("Send either receipts or the single-receipt fields (amounts, notes, date, apartments), not both.");
            items = list.EnumerateArray().ToList();
            if (items.Count == 0) return ToolResult.Fail("receipts needs at least one receipt.");
            if (items.Count > MaxReceiptsPerCall) return ToolResult.Fail($"Issue at most {MaxReceiptsPerCall} receipts at once.");
        }
        else items.Add(args);

        // Who can be billed: asked once for all the receipts.
        var approved = await Get("appartment/approved/get", s);
        if (!approved.Ok) return ToolResult.Fail(approved.Text);
        if (approved.IsMarker("NoAppartments")) return ToolResult.Fail("There are no approved apartments to bill yet. Approve join requests first.");
        var all = ApartmentRows(approved.Json()).ToList();

        // Check every receipt before anything is issued, so a mistake in the second never leaves the first half done.
        var specs = new List<ReceiptSpec>();
        for (var i = 0; i < items.Count; i++)
        {
            var (spec, error) = ParseReceipt(items[i], format!, all, multiple ? i + 1 : null);
            if (error is not null) return error;
            specs.Add(spec!);
        }

        if (!Bool(args, "confirm"))
        {
            if (!multiple)
            {
                var one = specs[0];
                return new ToolResult(
                    $"Preview, nothing has been issued yet. A receipt of {Money(one.Total, one.Currency)} dated {When(one)} ({string.Join(", ", one.Lines)}) for {Who(one, all)}; " +
                    $"each apartment owes {Money(one.Total, one.Currency)} and every resident gets a notification. Receipts can't be edited or deleted once they're added. " +
                    "Show this to the administrator. If they agree, call issue_receipt again with the same values and confirm=true.",
                    new { preview = true, currency = one.Currency, total = one.Total, date = one.Date ?? "today", lines = one.Lines, apartments = one.Targets.Select(t => t.Number), apartmentCount = one.Targets.Count });
            }

            var described = specs.Select((r, i) => $"({i + 1}) {Money(r.Total, r.Currency)} dated {When(r)} ({string.Join(", ", r.Lines)}) for {Who(r, all)}");
            return new ToolResult(
                $"Preview, nothing has been issued yet. {specs.Count} separate receipts: {string.Join("; ", described)}. " +
                "Amounts in different currencies are never added together. Each apartment owes each receipt it is billed for, and every resident gets a notification per receipt. " +
                "Receipts can't be edited or deleted once they're added. Show this to the administrator. If they agree, call issue_receipt again with the same values and confirm=true.",
                new
                {
                    preview = true,
                    receipts = specs.Select(r => new { currency = r.Currency, total = r.Total, date = r.Date ?? "today", lines = r.Lines, apartments = r.Targets.Select(t => t.Number), apartmentCount = r.Targets.Count }),
                });
        }

        var issued = new List<ReceiptSpec>();
        foreach (var r in specs)
        {
            var reply = await Post("receipt/add", new
            {
                currency = r.Currency,
                appartmentIds = r.Chosen.Count > 0 ? string.Join(",", r.Targets.Select(t => t.Id)) : "",
                version = "mcp-" + McpFunction.ServerVersion,
                receipt = new
                {
                    currency = r.Currency,
                    date = r.Date ?? "",
                    customFields = format!.Fields.Select(f => new { fieldName = f.Name, type = f.IsAmount ? "Amount" : "Text", value = r.Values.GetValueOrDefault(f.Name, "") }),
                },
            }, s);
            if (!reply.Ok)
            {
                if (issued.Count == 0) return ToolResult.Fail(reply.Text);
                // Some receipts are already out: say exactly which, so nothing is issued twice.
                return ToolResult.Fail($"{reply.Text}. Already issued, do not issue again: {string.Join("; ", issued.Select(x => Money(x.Total, x.Currency)))}. " +
                                       $"Not issued: {string.Join("; ", specs.Skip(issued.Count).Select(x => Money(x.Total, x.Currency)))}.");
            }
            issued.Add(r);
        }

        if (!multiple)
        {
            var one = specs[0];
            return new ToolResult($"Receipt added successfully. {Money(one.Total, one.Currency)} dated {When(one)} was issued to {Who(one, all)}, and their residents were notified.",
                new { issued = true, currency = one.Currency, total = one.Total, date = one.Date ?? "today", apartments = one.Targets.Select(t => t.Number), apartmentCount = one.Targets.Count });
        }

        var done = specs.Select(r => $"{Money(r.Total, r.Currency)} dated {When(r)} to {Who(r, all)}");
        return new ToolResult($"{specs.Count} receipts were issued: {string.Join("; ", done)}. Their residents were notified.",
            new { issued = true, receipts = specs.Select(r => new { currency = r.Currency, total = r.Total, date = r.Date ?? "today", apartments = r.Targets.Select(t => t.Number), apartmentCount = r.Targets.Count }) });
    }

    private static string When(ReceiptSpec r) => r.Date is null ? "today" : FormatDate(r.Date);

    private static string Who(ReceiptSpec r, List<ApartmentRow> all) =>
        r.Chosen.Count > 0 ? $"apartments {Join(r.Targets.Select(t => t.Number).ToList())}" : $"all {r.Targets.Count} approved {Plural(r.Targets.Count, "apartment")}";

    /// <summary>Checks one receipt against the building's format and apartments. `number` is its position when several are sent.</summary>
    private static (ReceiptSpec? Spec, ToolResult? Error) ParseReceipt(JsonElement item, ReceiptFormat format, List<ApartmentRow> all, int? number)
    {
        string Prefix(string text) => number is { } n ? $"Receipt {n}: {text}" : text;
        ToolResult Fail(string text) => ToolResult.Fail(Prefix(text));

        // Each receipt has one currency, USD or LBP; the building's receipt format only sets the default.
        var currency = format.Currency;
        var asked = Str(item, "currency");
        if (asked is not null)
        {
            currency = asked.ToUpperInvariant();
            if (currency is not ("USD" or "LBP")) return (null, Fail("currency must be USD or LBP."));
        }

        var byName = format.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        decimal total = 0;
        if (!item.TryGetProperty("amounts", out var amounts) || amounts.ValueKind != JsonValueKind.Object)
            return (null, Fail($"amounts is required, for example {{\"{format.Fields.FirstOrDefault(f => f.IsAmount)?.Name ?? "Maintenance"}\": 25}}."));
        foreach (var entry in amounts.EnumerateObject())
        {
            if (!byName.TryGetValue(entry.Name.Trim(), out var field) || !field.IsAmount)
                return (null, Fail($"\"{entry.Name}\" is not an amount field. Amount fields: {string.Join(", ", format.Fields.Where(f => f.IsAmount).Select(f => f.Name))}."));
            if (!TryAmount(entry.Value, out var amount)) return (null, Fail($"\"{field.Name}\" must be a number of at most 2 decimals, 0 or more."));
            values[field.Name] = amount.ToString(CultureInfo.InvariantCulture);
            total += amount;
        }
        if (item.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in notes.EnumerateObject())
            {
                if (!byName.TryGetValue(entry.Name.Trim(), out var field) || field.IsAmount)
                    return (null, Fail($"\"{entry.Name}\" is not a text field. Text fields: {string.Join(", ", format.Fields.Where(f => !f.IsAmount).Select(f => f.Name))}."));
                var text = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()!.Trim() : "";
                if (text.Length > 500) return (null, Fail($"\"{field.Name}\" is too long (500 characters at most)."));
                values[field.Name] = text;
            }
        }
        if (total <= 0) return (null, Fail("Enter at least one amount greater than 0."));

        var date = Str(item, "date");
        if (date is not null && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return (null, Fail("date must look like 2026-10-01."));

        // Who gets it: chosen apartments, or every approved apartment.
        var chosen = StrArray(item, "apartments");
        var targets = all;
        if (chosen.Count > 0)
        {
            var lookup = all.ToDictionary(a => a.Number.Trim(), StringComparer.OrdinalIgnoreCase);
            var unknown = chosen.Where(n => !lookup.ContainsKey(n.Trim())).ToList();
            if (unknown.Count > 0) return (null, Fail($"Not approved apartments of the building: {string.Join(", ", unknown)}."));
            targets = chosen.Select(n => lookup[n.Trim()]).DistinctBy(a => a.Id).ToList();
        }

        var lines = format.Fields.Where(f => values.ContainsKey(f.Name) && values[f.Name].Length > 0)
            .Select(f => f.IsAmount ? $"{f.Name} {FormatAmount(decimal.Parse(values[f.Name], CultureInfo.InvariantCulture), currency)}" : $"{f.Name}: {values[f.Name]}")
            .ToList();
        return (new ReceiptSpec(currency, values, total, date, chosen, targets, lines), null);
    }

    // ==================================================================== loading

    /// <summary>The collection summary for a receipt (default: newest), or a ready answer when there is none.</summary>
    private async Task<(JsonElement? Summary, ToolResult? Stop)> LoadSummaryAsync(int? receiptId, McpSession s)
    {
        var reply = await Get("collection/summary" + (receiptId is { } id ? $"?receiptId={id}" : ""), s);
        if (!reply.Ok) return (null, ToolResult.Fail(reply.Text));
        var summary = reply.Json();
        if (!summary.GetProperty("hasBuilding").GetBoolean()) return (null, new ToolResult("Set up your building in the Baytak app first."));
        if (summary.GetProperty("selected").ValueKind != JsonValueKind.Object) return (null, new ToolResult("No receipts have been issued yet."));
        return (summary, null);
    }

    private async Task<(ReceiptFormat? Format, ToolResult? Stop)> LoadFormatAsync(McpSession s)
    {
        var reply = await Get("get-receipt-format", s);
        if (!reply.Ok) return (null, ToolResult.Fail(reply.Text));
        if (reply.IsMarker("SetupBuilding")) return (null, ToolResult.Fail("Set up your building in the Baytak app first."));
        if (reply.IsMarker("SetupReceipt")) return (null, ToolResult.Fail("Set up your receipt format (currency and fields) in the Baytak app first."));

        using var doc = JsonDocument.Parse(reply.Text);
        var root = doc.RootElement;
        var fields = root.GetProperty("customFields").EnumerateArray()
            .Select(f => new FormatField(f.GetProperty("fieldName").GetString() ?? "", string.Equals(f.GetProperty("type").GetString(), "Amount", StringComparison.OrdinalIgnoreCase)))
            .Where(f => f.Name.Length > 0).ToList();
        return (new ReceiptFormat(root.GetProperty("currency").GetString() ?? "USD", fields), null);
    }

    /// <summary>An apartment row from the API lists, without the phone numbers and emergency contacts they also carry.</summary>
    private sealed record ApartmentRow(int Id, string Number, int Floor, string? Resident, string? Description);

    private static IEnumerable<ApartmentRow> ApartmentRows(JsonElement list) =>
        list.EnumerateArray().Select(a => new ApartmentRow(
            a.GetProperty("id").GetInt32(),
            a.GetProperty("appartmentNumber").GetString() ?? "",
            a.GetProperty("floor").GetInt32(),
            NullIfEmpty(a.GetProperty("fullName").GetString()),
            NullIfEmpty(a.GetProperty("appartmentDescription").GetString())));

    private static (string? Date, List<Line> Lines) ParseReceiptBody(string? json)
    {
        var lines = new List<Line>();
        if (string.IsNullOrEmpty(json)) return (null, lines);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var date = root.TryGetProperty("Date", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            if (date is { Length: >= 10 }) date = date[..10];
            if (root.TryGetProperty("CustomFields", out var fields) && fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fields.EnumerateArray())
                {
                    var name = f.TryGetProperty("FieldName", out var n) ? n.GetString() ?? "" : "";
                    var isAmount = f.TryGetProperty("Type", out var t) && string.Equals(t.GetString(), "Amount", StringComparison.OrdinalIgnoreCase);
                    var raw = f.TryGetProperty("Value", out var v) ? v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString() ?? "" : "";
                    if (isAmount)
                    {
                        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount != 0) lines.Add(new Line(name, amount, null));
                    }
                    else if (raw.Trim().Length > 0)
                    {
                        lines.Add(new Line(name, null, raw.Trim()));
                    }
                }
            }
            return (date, lines);
        }
        catch (JsonException)
        {
            return (null, lines);
        }
    }

    // ==================================================================== formatting (matches the app)

    private static string Summary(MyReceipt r) =>
        $"{FormatDate(r.Date)}: {Money(r.Total, r.Currency)}" +
        (r.Lines.Count == 0 ? "" : $" ({string.Join(", ", r.Lines.Select(l => l.Amount is { } a ? $"{l.Name} {FormatAmount(a, r.Currency)}" : $"{l.Name}: {l.Note}"))})");

    private static object ReceiptData(MyReceipt r) => new
    {
        receiptId = r.ReceiptId,
        date = r.Date,
        total = r.Total,
        currency = r.Currency,
        status = r.Paid ? "Paid" : "Unpaid",
        lines = r.Lines.Where(l => l.Amount is not null).Select(l => new { name = l.Name, amount = l.Amount }),
        notes = r.Lines.Where(l => l.Note is not null).Select(l => new { name = l.Name, text = l.Note }),
    };

    /// <summary>"1,500,000 LBP", "75 USD", "1,250.50 USD": cents only for USD amounts that have them.</summary>
    private static string Money(decimal amount, string currency) => $"{FormatAmount(amount, currency)} {currency}";

    private static string FormatAmount(decimal amount, string currency)
    {
        var rounded = Math.Round(amount, 2);
        var whole = rounded == decimal.Truncate(rounded);
        return currency == "USD" && !whole ? rounded.ToString("#,##0.00", CultureInfo.InvariantCulture) : rounded.ToString("#,##0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatDate(string? ymd) =>
        DateOnly.TryParseExact(ymd?.Length >= 10 ? ymd[..10] : ymd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMM d, yyyy", English)
            : ymd ?? "";

    private static string OrdinalFloor(int n)
    {
        var mod100 = n % 100;
        var suffix = mod100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix} floor";
    }

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";

    private static string Join(List<string> items) =>
        items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    // ==================================================================== arguments

    private static string? Str(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;

    private static int? Int(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i
        : args.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out i) ? i
        : null;

    private static bool Bool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true"));

    private static List<string> StrArray(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(x => x.Length > 0).ToList()
            : v.ValueKind == JsonValueKind.String && v.GetString()!.Trim().Length > 0 ? [v.GetString()!.Trim()] : [];

    private static bool TryAmount(JsonElement value, out decimal amount)
    {
        amount = 0;
        var ok = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out amount),
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount),
            _ => false,
        };
        return ok && amount >= 0 && amount <= 1_000_000_000_000m && decimal.Round(amount, 2) == amount;
    }

    private Task<ApiReply> Get(string path, McpSession s) => _api.CallAsync(HttpMethod.Get, path, s.Jwt, null, s.Ct);

    private Task<ApiReply> Post(string path, object body, McpSession s) => _api.CallAsync(HttpMethod.Post, path, s.Jwt, body, s.Ct);
}
