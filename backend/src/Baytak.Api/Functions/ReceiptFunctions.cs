using System.Globalization;
using System.Text.Json;
using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Domain;
using Baytak.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baytak.Api.Functions;

public sealed class ReceiptFieldDto
{
    public string? FieldName { get; set; }
    /// <summary>"Amount" or "Text".</summary>
    public string? Type { get; set; }
    /// <summary>Strings or numbers are both accepted.</summary>
    public JsonElement? Value { get; set; }
}

public sealed class ReceiptFormatRequest
{
    public string? Currency { get; set; }
    public List<ReceiptFieldDto>? CustomFields { get; set; }
}

public sealed class ReceiptBodyDto
{
    public string? Currency { get; set; }
    public List<ReceiptFieldDto>? CustomFields { get; set; }
    public string? Date { get; set; }
}

public sealed class AddReceiptRequest
{
    public string? Currency { get; set; }
    public ReceiptBodyDto? Receipt { get; set; }
    public string? AppartmentIds { get; set; }
    public string? Version { get; set; }
}

public sealed class SaveStatusRequest
{
    public int? AppartmentId { get; set; }
    public int? ReceiptId { get; set; }
    public string? Status { get; set; }
}

/// <summary>Receipt formats, issuing receipts to apartments, and payment tracking.</summary>
public sealed class ReceiptFunctions : ApiFunction
{
    private const int MaxFields = 30;
    private const int MaxFieldNameLength = 60;
    private const int MaxTextLength = 500;
    private const string RegisterMarker = "Register Apaprtment"; // legacy spelling, matched by the app

    private readonly BaytakDbContext _db;
    private readonly IPushSender _push;
    private readonly TimeProvider _time;

    public ReceiptFunctions(IAccessGuard guard, BaytakDbContext db, IPushSender push, TimeProvider time, ILogger<ReceiptFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _push = push;
        _time = time;
    }

    // ------------------------------------------------------------- receipt format (admin)

    /// <summary>
    /// A JSON string with the saved format, or one of the marker strings
    /// "SetupBuilding" (no building yet) / "SetupReceipt" (no format yet).
    /// </summary>
    [Function("ReceiptFormatGet")]
    public Task<IActionResult> GetFormat(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "get-receipt-format")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.Message("SetupBuilding");

            var format = await _db.ReceiptFormats.AsNoTracking().FirstOrDefaultAsync(f => f.BuildingId == building.Id, ct);
            if (format is null) return Reply.Message("SetupReceipt");

            var fields = JsonSerializer.Deserialize<List<StoredField>>(format.FieldsJson, Reply.Json) ?? [];
            var payload = new
            {
                currency = format.Currency,
                customFields = fields.Select(f => new { fieldName = f.FieldName, type = f.Type, value = "" }),
                Date = "",
            };
            // The app expects the format itself serialised as a JSON string.
            return Reply.Message(JsonSerializer.Serialize(payload, Reply.Json));
        });

    [Function("ReceiptFormatSave")]
    public Task<IActionResult> SaveFormat(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "save-receipt-format")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (body, error) = await Reply.ReadJsonAsync<ReceiptFormatRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);

            var building = await _db.Buildings.FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.BadRequest("Please set up your building first");

            var currency = Money.NormalizeCurrency(body.Currency);
            if (currency is null) return Reply.BadRequest("Please select a currency (USD or LBP)");

            var (fields, fieldError) = NormalizeFields(body.CustomFields, requireAmount: true);
            if (fields is null) return Reply.BadRequest(fieldError!);

            var format = await _db.ReceiptFormats.FirstOrDefaultAsync(f => f.BuildingId == building.Id, ct);
            if (format is null)
            {
                format = new ReceiptFormat { BuildingId = building.Id };
                _db.ReceiptFormats.Add(format);
            }
            format.Currency = currency;
            format.FieldsJson = JsonSerializer.Serialize(fields.Select(f => new StoredField(f.Name, f.IsAmount ? "Amount" : "Text")), Reply.Json);
            format.UpdatedAt = _time.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
            return Reply.Message("Receipt format saved successfully");
        });

    // ------------------------------------------------------------- issuing receipts (admin)

    [Function("ReceiptAdd")]
    public Task<IActionResult> Add(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "receipt/add")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (body, error) = await Reply.ReadJsonAsync<AddReceiptRequest>(req, ct);
            if (body?.Receipt is null) return Reply.BadRequest(error ?? "Receipt is required");

            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.BadRequest("Please set up your building first");

            var format = await _db.ReceiptFormats.AsNoTracking().FirstOrDefaultAsync(f => f.BuildingId == building.Id, ct);
            var currency = Money.NormalizeCurrency(body.Receipt.Currency ?? body.Currency) ?? format?.Currency;
            if (currency is null) return Reply.BadRequest("Please set up your receipt format first");

            var (fields, fieldError) = NormalizeFields(body.Receipt.CustomFields, requireAmount: false);
            if (fields is null) return Reply.BadRequest(fieldError!);

            // The total is always recomputed here: the client's figure is never trusted.
            decimal total = 0;
            foreach (var f in fields.Where(f => f.IsAmount))
            {
                if (!TryParseAmount(f.Value, out var amount)) return Reply.BadRequest($"\"{f.Name}\" must be a valid amount (up to 2 decimals)");
                total += amount;
            }
            if (total <= 0) return Reply.BadRequest("Enter at least one amount greater than 0");

            var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
            var date = today;
            if (!string.IsNullOrWhiteSpace(body.Receipt.Date) && !EntryValidator.TryParseDate(body.Receipt.Date, out date))
                return Reply.BadRequest("Receipt date is not valid");
            if (date < new DateOnly(2000, 1, 1) || date > today.AddYears(1)) return Reply.BadRequest("Receipt date is out of range");

            // Targets: the selected apartments, or every approved apartment when none are selected.
            var requestedIds = ParseIds(body.AppartmentIds);
            if (requestedIds is null) return Reply.BadRequest("Selected apartments are not valid");

            var targets = _db.Apartments.Include(a => a.User).Where(a => a.BuildingId == building.Id && a.Status == ApartmentStatuses.Approved);
            if (requestedIds.Count > 0) targets = targets.Where(a => requestedIds.Contains(a.Id));
            var apartments = await targets.ToListAsync(ct);
            if (requestedIds.Count > 0 && apartments.Count != requestedIds.Count)
                return Reply.BadRequest("One or more selected apartments are not part of your building");
            if (apartments.Count == 0) return Reply.BadRequest("There are no approved apartments to issue this receipt to");

            var receipt = new Receipt
            {
                BuildingId = building.Id,
                CreatedByUserId = admin.Id,
                Currency = currency,
                Total = total,
                Date = date,
                Version = CleanVersion(body.Version),
                CreatedAt = _time.GetUtcNow().UtcDateTime,
                // The shape the app reads back (PascalCase), see ParseReceipt in the mobile app.
                BodyJson = JsonSerializer.Serialize(new
                {
                    Currency = currency,
                    Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00",
                    CustomFields = fields.Select(f => new { FieldName = f.Name, Type = f.IsAmount ? "Amount" : "Text", f.Value }),
                }),
                Assignments = apartments.Select(a => new ReceiptAssignment { ApartmentId = a.Id, Status = PaymentStatuses.Pending }).ToList(),
            };
            _db.Receipts.Add(receipt);
            await _db.SaveChangesAsync(ct);

            await _push.SendAsync(
                apartments.Select(a => a.User?.ExpoPushToken),
                "New receipt",
                $"{building.BuildingName} issued a new receipt of {total.ToString("0.##", CultureInfo.InvariantCulture)} {currency}.",
                ct);

            return Reply.Message("Receipt added successfully");
        });

    [Function("ReceiptSaveStatus")]
    public Task<IActionResult> SaveStatus(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "receipt/appartment/save-status")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (body, error) = await Reply.ReadJsonAsync<SaveStatusRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);
            if (body.AppartmentId is not { } apartmentId || body.ReceiptId is not { } receiptId)
                return Reply.BadRequest("Apartment and receipt are required");

            var status = body.Status?.Trim().ToLowerInvariant() switch
            {
                "paid" => PaymentStatuses.Paid,
                "pending" => PaymentStatuses.Pending,
                _ => null,
            };
            if (status is null) return Reply.BadRequest("Status must be Paid or Pending");

            var assignment = await _db.ReceiptAssignments
                .Include(a => a.Apartment).ThenInclude(a => a!.Building)
                .FirstOrDefaultAsync(a => a.ReceiptId == receiptId && a.ApartmentId == apartmentId
                                          && a.Apartment!.Building!.AdminUserId == admin.Id, ct);
            if (assignment is null) return Reply.NotFound("Receipt not found");

            assignment.Status = status;
            assignment.PaidAt = status == PaymentStatuses.Paid ? _time.GetUtcNow().UtcDateTime : null;
            await _db.SaveChangesAsync(ct);
            return Reply.Message($"Receipt status changed to {status}");
        });

    /// <summary>All receipts issued to one apartment of the administrator's building.</summary>
    [Function("ReceiptsForApartment")]
    public Task<IActionResult> ForApartment(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "receipts/appartment/{id:int}")] HttpRequest req, int id, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var apt = await _db.Apartments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.Building!.AdminUserId == admin.Id, ct);
            if (apt is null) return Reply.NotFound("Apartment not found");
            return Reply.Ok(await ReceiptsOfAsync(apt, limit: 500, ct));
        });

    // ------------------------------------------------------------- residents

    /// <summary>The newest receipts of the signed-in resident's apartment (dashboard).</summary>
    [Function("MyReceiptsLatest")]
    public Task<IActionResult> Latest(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "appartment/receipt")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, user => MyReceiptsAsync(user, limit: 5, ct));

    [Function("MyReceiptsAll")]
    public Task<IActionResult> All(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "appartment/receipts")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, user => MyReceiptsAsync(user, limit: 500, ct));

    /// <summary>Amounts per receipt for the last 12 receipts (used for charts).</summary>
    [Function("MyReceiptHistory")]
    public Task<IActionResult> History(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "receipt/history")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var apt = await _db.Apartments.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == user.Id && a.Status == ApartmentStatuses.Approved, ct);
            if (apt is null) return Reply.Message(RegisterMarker);

            var rows = await _db.ReceiptAssignments.AsNoTracking()
                .Where(x => x.ApartmentId == apt.Id)
                .OrderByDescending(x => x.Receipt!.Date).ThenByDescending(x => x.Id)
                .Take(12)
                .Select(x => new { x.Receipt!.Date, x.Receipt.Currency, x.Receipt.Total, x.Receipt.BodyJson })
                .ToListAsync(ct);

            return Reply.Ok(rows.OrderBy(r => r.Date).Select(r => new
            {
                date = r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                currency = r.Currency,
                total = r.Total,
                receiptValues = ReadFields(r.BodyJson).Where(f => f.IsAmount).Select(f => new { fieldName = f.Name, value = TryParseAmount(f.Value, out var v) ? v : 0m }),
            }));
        });

    // ------------------------------------------------------------- helpers

    private async Task<IActionResult> MyReceiptsAsync(User user, int limit, CancellationToken ct)
    {
        var apt = await _db.Apartments.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == user.Id && a.Status == ApartmentStatuses.Approved, ct);
        if (apt is null) return Reply.Message(RegisterMarker);

        var items = await ReceiptsOfAsync(apt, limit, ct);
        if (items.Count > 0) return Reply.Ok(items);

        // Registered but nothing issued yet: the app still needs the apartment details.
        return Reply.Ok(new[] { new { appartment = ApartmentDto(apt), receipt = (string?)null } });
    }

    private async Task<List<object>> ReceiptsOfAsync(Apartment apt, int limit, CancellationToken ct)
    {
        var rows = await _db.ReceiptAssignments.AsNoTracking()
            .Where(x => x.ApartmentId == apt.Id)
            .OrderByDescending(x => x.Receipt!.Date).ThenByDescending(x => x.Id)
            .Take(limit)
            .Select(x => new { x.Id, x.ReceiptId, x.Status, x.Receipt!.Total, x.Receipt.Currency, x.Receipt.BodyJson })
            .ToListAsync(ct);

        var apartment = ApartmentDto(apt);
        return rows.Select(r => (object)new
        {
            id = r.Id,
            receiptId = r.ReceiptId,
            appartment = apartment,
            receipt = r.BodyJson,
            status = r.Status,
            totalPayment = r.Total,
            currency = r.Currency,
        }).ToList();
    }

    private static object ApartmentDto(Apartment a) => new
    {
        id = a.Id,
        appartmentNumber = a.ApartmentNumber,
        floor = a.Floor,
        appartmentDescription = a.Description,
    };

    private static string CleanVersion(string? raw)
    {
        var text = Text.Clean(raw);
        return text.Length == 0 ? "v2.0" : text[..Math.Min(16, text.Length)];
    }

    private sealed record StoredField(string FieldName, string Type);
    private sealed record Field(string Name, bool IsAmount, string Value);

    private static List<int>? ParseIds(string? raw)
    {
        var ids = new List<int>();
        foreach (var part in (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) return null;
            if (!ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Trims, bounds and de-duplicates the field list shared by formats and receipts.</summary>
    private static (List<Field>? Fields, string? Error) NormalizeFields(List<ReceiptFieldDto>? input, bool requireAmount)
    {
        if (input is null || input.Count == 0) return (null, "Add at least one field");
        if (input.Count > MaxFields) return (null, $"A receipt can have at most {MaxFields} fields");

        var fields = new List<Field>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in input)
        {
            var name = Text.Clean(f.FieldName);
            if (name.Length == 0) return (null, "Every field needs a name");
            if (name.Length > MaxFieldNameLength) return (null, $"Field names can have at most {MaxFieldNameLength} characters");
            if (!seen.Add(name)) return (null, "Field names must be unique");

            var type = f.Type?.Trim().ToLowerInvariant();
            if (type is not ("amount" or "text")) return (null, $"\"{name}\" must be an Amount or Text field");

            var value = f.Value is { } v ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                _ => "",
            } : "";
            value = type == "text" ? Text.Clean(value, keepNewlines: true) : value.Trim();
            if (value.Length > MaxTextLength) return (null, $"\"{name}\" is too long");

            fields.Add(new Field(name, type == "amount", value));
        }
        if (requireAmount && !fields.Any(f => f.IsAmount)) return (null, "Add at least one Amount field");
        return (fields, null);
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value)) return true; // blank amount = 0
        if (!decimal.TryParse(value.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) return false;
        return amount >= 0 && amount <= EntryValidator.MaxAmount && decimal.Round(amount, 2) == amount;
    }

    private static List<Field> ReadFields(string bodyJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(bodyJson);
            if (!doc.RootElement.TryGetProperty("CustomFields", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            return list.EnumerateArray().Select(e => new Field(
                e.GetProperty("FieldName").GetString() ?? "",
                string.Equals(e.GetProperty("Type").GetString(), "Amount", StringComparison.OrdinalIgnoreCase),
                e.TryGetProperty("Value", out var v) ? v.GetString() ?? "" : "")).ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }
}
