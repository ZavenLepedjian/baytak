using System.Globalization;
using Baytak.Api.Common;
using Baytak.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baytak.Api.Functions;

/// <summary>
/// The administrator's home screen in one call: how the building is doing across receipts, who needs attention,
/// and what came in recently.
///   GET  building/overview
/// (Not "admin/overview": the Functions host reserves the admin prefix and answered 404 before the function ran.)
/// </summary>
public sealed class OverviewFunctions : ApiFunction
{
    /// <summary>
    /// A receipt counts as late when it is still unpaid, or was paid, more than this many days after its date.
    /// Receipts have no due date, so their date is the only clock there is.
    /// </summary>
    public const int LateAfterDays = 14;
    /// <summary>The newest receipts that feed the "needs attention" and "recent payments" views.</summary>
    public const int Window = 8;
    public const int ListSize = 5;

    private readonly BaytakDbContext _db;
    private readonly TimeProvider _time;

    public OverviewFunctions(IAccessGuard guard, BaytakDbContext db, TimeProvider time, ILogger<OverviewFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _time = time;
    }

    private sealed record Row(int ReceiptId, int ApartmentId, string ApartmentNumber, int Floor, string FirstName, string LastName, string Status, DateTime? PaidAt);

    [Function("AdminOverview")]
    public Task<IActionResult> Overview(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "building/overview")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.Ok(new { hasBuilding = false });

            var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
            var apartmentCount = await _db.Apartments.CountAsync(a => a.BuildingId == building.Id && a.Status == ApartmentStatuses.Approved, ct);
            var pendingRequests = await _db.Apartments.CountAsync(a => a.BuildingId == building.Id && a.Status == ApartmentStatuses.Pending, ct);

            var recent = await _db.Receipts.AsNoTracking()
                .Where(r => r.BuildingId == building.Id)
                .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
                .Take(Window)
                .Select(r => new
                {
                    r.Id,
                    r.Date,
                    r.Currency,
                    r.Total,
                    Count = r.Assignments.Count,
                    Paid = r.Assignments.Count(a => a.Status == PaymentStatuses.Paid),
                })
                .ToListAsync(ct);
            var meta = recent.ToDictionary(r => r.Id, r => (r.Date, r.Currency, r.Total));
            var ids = meta.Keys.ToList();

            var rows = await _db.ReceiptAssignments.AsNoTracking()
                .Where(a => ids.Contains(a.ReceiptId))
                .Select(a => new Row(a.ReceiptId, a.ApartmentId, a.Apartment!.ApartmentNumber, a.Apartment.Floor,
                    a.Apartment.User!.FirstName, a.Apartment.User.LastName, a.Status, a.PaidAt))
                .ToListAsync(ct);

            // What is still owed across every receipt of the building, not only the recent ones.
            var unpaidAll = await _db.ReceiptAssignments.AsNoTracking()
                .Where(a => a.Receipt!.BuildingId == building.Id && a.Status != PaymentStatuses.Paid)
                .Select(a => new { a.ApartmentId, a.Receipt!.Currency, a.Receipt.Total })
                .ToListAsync(ct);
            var outstanding = unpaidAll
                .GroupBy(x => x.Currency)
                .OrderBy(g => g.Key == "USD" ? 0 : 1).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new
                {
                    currency = g.Key,
                    amount = g.Sum(x => x.Total),
                    apartments = g.Select(x => x.ApartmentId).Distinct().Count(),
                    receipts = g.Count(),
                })
                .ToList();

            var latest = recent.Count == 0 ? null : recent[0];

            return Reply.Ok(new
            {
                hasBuilding = true,
                buildingName = building.BuildingName,
                apartmentCount,
                pendingRequests,
                lateAfterDays = LateAfterDays,
                latest = latest is null ? (object?)null : new
                {
                    id = latest.Id,
                    date = Day(latest.Date),
                    currency = latest.Currency,
                    total = latest.Total,
                    count = latest.Count,
                    paidCount = latest.Paid,
                    unpaidCount = latest.Count - latest.Paid,
                    collected = latest.Paid * latest.Total,
                    expected = latest.Count * latest.Total,
                },
                outstanding,
                needsAttention = NeedsAttention(rows, meta, today),
                recentPayments = RecentPayments(rows, meta),
                receipts = recent.Take(ListSize).Select(r => new
                {
                    id = r.Id,
                    date = Day(r.Date),
                    currency = r.Currency,
                    total = r.Total,
                    count = r.Count,
                    paidCount = r.Paid,
                }).ToList(),
            });
        });

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Name(Row r) => $"{r.FirstName} {r.LastName}".Trim();

    /// <summary>Apartments that are overdue now, or that tend to pay long after the receipt date, worst first.</summary>
    private static List<object> NeedsAttention(List<Row> rows, Dictionary<int, (DateOnly Date, string Currency, decimal Total)> receipts, DateOnly today)
    {
        var result = new List<(int Score, int Oldest, int Floor, string Number, object Item)>();
        foreach (var group in rows.GroupBy(r => r.ApartmentId))
        {
            var first = group.First();
            var unpaidDays = new List<int>();
            var owed = new Dictionary<string, decimal>();
            var latePaid = 0;
            foreach (var r in group)
            {
                var receipt = receipts[r.ReceiptId];
                if (r.Status != PaymentStatuses.Paid)
                {
                    unpaidDays.Add(today.DayNumber - receipt.Date.DayNumber);
                    owed[receipt.Currency] = owed.GetValueOrDefault(receipt.Currency) + receipt.Total;
                }
                else if (r.PaidAt is { } paidAt && DateOnly.FromDateTime(paidAt).DayNumber - receipt.Date.DayNumber > LateAfterDays)
                {
                    latePaid++;
                }
            }

            var overdue = unpaidDays.Count(d => d > LateAfterDays);
            var score = overdue + latePaid;
            if (score == 0) continue;

            var oldest = unpaidDays.Count == 0 ? 0 : Math.Max(0, unpaidDays.Max());
            result.Add((score, oldest, first.Floor, first.ApartmentNumber, new
            {
                apartmentId = first.ApartmentId,
                apartmentNumber = first.ApartmentNumber,
                floor = first.Floor,
                residentName = Name(first),
                unpaidCount = unpaidDays.Count,
                overdueCount = overdue,
                oldestUnpaidDays = oldest,
                latePaidCount = latePaid,
                owed = owed
                    .OrderBy(o => o.Key == "USD" ? 0 : 1).ThenBy(o => o.Key, StringComparer.Ordinal)
                    .Select(o => new { currency = o.Key, amount = o.Value })
                    .ToList(),
            }));
        }

        return result
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Oldest).ThenBy(x => x.Floor).ThenBy(x => x.Number, StringComparer.OrdinalIgnoreCase)
            .Take(ListSize)
            .Select(x => x.Item)
            .ToList();
    }

    private static List<object> RecentPayments(List<Row> rows, Dictionary<int, (DateOnly Date, string Currency, decimal Total)> receipts) =>
        rows.Where(r => r.Status == PaymentStatuses.Paid && r.PaidAt is not null)
            .OrderByDescending(r => r.PaidAt)
            .Take(ListSize)
            .Select(r => (object)new
            {
                apartmentNumber = r.ApartmentNumber,
                floor = r.Floor,
                residentName = Name(r),
                currency = receipts[r.ReceiptId].Currency,
                amount = receipts[r.ReceiptId].Total,
                receiptDate = Day(receipts[r.ReceiptId].Date),
                paidAt = r.PaidAt,
            })
            .ToList();
}
