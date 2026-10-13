using System.Globalization;
using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baytak.Api.Functions;

public sealed class RemindRequest
{
    public int? ReceiptId { get; set; }
}

/// <summary>
/// The administrator's collection view: who has paid a given receipt, and a push reminder for those who haven't.
///   GET  collection/summary?receiptId=   the last receipts plus per-apartment paid/unpaid for one of them
///   POST collection/remind               push a reminder to every resident still unpaid (once per receipt per day)
/// </summary>
public sealed class CollectionFunctions : ApiFunction
{
    public const int RecentReceipts = 6;
    public static readonly TimeSpan ReminderCooldown = TimeSpan.FromHours(24);

    private readonly BaytakDbContext _db;
    private readonly IPushSender _push;
    private readonly TimeProvider _time;

    public CollectionFunctions(IAccessGuard guard, BaytakDbContext db, IPushSender push, TimeProvider time, ILogger<CollectionFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _push = push;
        _time = time;
    }

    [Function("CollectionSummary")]
    public Task<IActionResult> Summary(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "collection/summary")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.Ok(new { hasBuilding = false, receipts = Array.Empty<object>(), selected = (object?)null });

            // The receipts that can be switched between: newest first, with how many apartments paid each.
            var recent = await _db.Receipts.AsNoTracking()
                .Where(r => r.BuildingId == building.Id)
                .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
                .Take(RecentReceipts)
                .Select(r => new
                {
                    r.Id,
                    r.Date,
                    r.Currency,
                    r.Total,
                    r.LastReminderAt,
                    Count = r.Assignments.Count,
                    Paid = r.Assignments.Count(a => a.Status == PaymentStatuses.Paid),
                })
                .ToListAsync(ct);

            var receipts = recent.Select(r => new
            {
                id = r.Id,
                date = r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                currency = r.Currency,
                total = r.Total,
                count = r.Count,
                paidCount = r.Paid,
            }).ToList();

            // Default to the newest; an explicit id must be one of this building's receipts (not just recent ones).
            var requested = req.Query["receiptId"].ToString();
            if (recent.Count == 0)
            {
                return string.IsNullOrEmpty(requested)
                    ? Reply.Ok(new { hasBuilding = true, receipts, selected = (object?)null })
                    : Reply.NotFound("Receipt not found");
            }

            var receiptId = recent[0].Id;
            if (!string.IsNullOrEmpty(requested))
            {
                if (!int.TryParse(requested, NumberStyles.None, CultureInfo.InvariantCulture, out receiptId)) return Reply.BadRequest("Invalid receipt id");
            }

            var receipt = await _db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == receiptId && r.BuildingId == building.Id, ct);
            if (receipt is null) return Reply.NotFound("Receipt not found");

            var rows = await _db.ReceiptAssignments.AsNoTracking()
                .Where(a => a.ReceiptId == receipt.Id)
                .Select(a => new
                {
                    a.ApartmentId,
                    a.Apartment!.ApartmentNumber,
                    a.Apartment.Floor,
                    FirstName = a.Apartment.User!.FirstName,
                    LastName = a.Apartment.User.LastName,
                    Phone = a.Apartment.User.PhoneNumber,
                    a.Status,
                    a.PaidAt,
                })
                .ToListAsync(ct);

            var apartments = rows
                .OrderBy(r => r.Floor).ThenBy(r => r.ApartmentNumber, StringComparer.OrdinalIgnoreCase)
                .Select(r => new
                {
                    apartmentId = r.ApartmentId,
                    apartmentNumber = r.ApartmentNumber,
                    floor = r.Floor,
                    residentName = $"{r.FirstName} {r.LastName}".Trim(),
                    phoneNumber = r.Phone,
                    status = r.Status,
                    paidAt = r.PaidAt,
                })
                .ToList();

            var paid = rows.Count(r => r.Status == PaymentStatuses.Paid);
            var coolsDown = receipt.LastReminderAt is { } last && _time.GetUtcNow().UtcDateTime - last < ReminderCooldown;

            return Reply.Ok(new
            {
                hasBuilding = true,
                receipts,
                selected = new
                {
                    id = receipt.Id,
                    date = receipt.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    currency = receipt.Currency,
                    total = receipt.Total,
                    count = rows.Count,
                    paidCount = paid,
                    unpaidCount = rows.Count - paid,
                    collected = paid * receipt.Total,
                    expected = rows.Count * receipt.Total,
                    lastRemindedAt = receipt.LastReminderAt,
                    canRemind = rows.Count - paid > 0 && !coolsDown,
                    apartments,
                },
            });
        });

    [Function("CollectionRemind")]
    public Task<IActionResult> Remind(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "collection/remind")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (body, error) = await Reply.ReadJsonAsync<RemindRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);
            if (body.ReceiptId is not { } receiptId) return Reply.BadRequest("Receipt is required");

            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            var receipt = building is null
                ? null
                : await _db.Receipts.FirstOrDefaultAsync(r => r.Id == receiptId && r.BuildingId == building.Id, ct);
            if (receipt is null || building is null) return Reply.NotFound("Receipt not found");

            var now = _time.GetUtcNow().UtcDateTime;
            if (receipt.LastReminderAt is { } last && now - last < ReminderCooldown)
                return Reply.Text(StatusCodes.Status429TooManyRequests, "You already sent a reminder for this receipt today. Please try again tomorrow.");

            var tokens = await _db.ReceiptAssignments.AsNoTracking()
                .Where(a => a.ReceiptId == receipt.Id && a.Status == PaymentStatuses.Pending)
                .Select(a => a.Apartment!.User!.ExpoPushToken)
                .ToListAsync(ct);
            if (tokens.Count == 0) return Reply.BadRequest("Everyone has paid this receipt.");

            // Claim the day's reminder before sending, so a retry or double tap cannot message residents twice.
            receipt.LastReminderAt = now;
            await _db.SaveChangesAsync(ct);

            await _push.SendAsync(
                tokens,
                "Payment reminder",
                $"{building.BuildingName}: your receipt of {receipt.Total.ToString("0.##", CultureInfo.InvariantCulture)} {receipt.Currency} from {receipt.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is still unpaid.",
                ct);

            return Reply.Ok(new { sent = tokens.Count });
        });
}
