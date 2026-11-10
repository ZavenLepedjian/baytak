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
/// The one-page summary of a month of collection.
///   GET  building/summary?month=2026-10
///        billed, collected and still owed for the month, per currency. For the administrator and for residents of an
///        approved apartment: it holds totals only, never who has paid or who owes.
/// Everything comes from the receipts, so it is as complete as the receipts are.
/// </summary>
public sealed class SummaryFunctions : ApiFunction
{
    private static readonly DateOnly MinDate = new(2020, 1, 1);

    private readonly BaytakDbContext _db;
    private readonly TimeProvider _time;

    public SummaryFunctions(IAccessGuard guard, BaytakDbContext db, TimeProvider time, ILogger<SummaryFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _time = time;
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

    private sealed record Row(int ReceiptId, int ApartmentId, string Currency, decimal Total, DateOnly ReceiptDate, string Status, DateTime? PaidAt);

    [Function("BuildingSummary")]
    public Task<IActionResult> Summary(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "building/summary")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var building = await FindBuildingAsync(user, ct);
            if (building is null) return Reply.Ok(new { hasBuilding = false });

            if (!TryParseMonth(req.Query["month"], out var start)) start = new DateOnly(Today.Year, Today.Month, 1);
            var next = start.AddMonths(1);
            var nextUtc = next.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            // Every receipt issued to every apartment of the building: one small row each. Collected and still-owed depend on
            // when each was paid, so they are worked out here rather than in SQL.
            var rows = await _db.ReceiptAssignments.AsNoTracking()
                .Where(a => a.Receipt!.BuildingId == building.Id)
                .Select(a => new Row(a.ReceiptId, a.ApartmentId, a.Receipt!.Currency, a.Receipt.Total, a.Receipt.Date, a.Status, a.PaidAt))
                .ToListAsync(ct);

            var earliest = rows.Count == 0 ? start : rows.Min(r => r.ReceiptDate);

            var currencies = rows.Select(r => r.Currency).Distinct()
                .OrderBy(c => c == "USD" ? 0 : 1).ThenBy(c => c, StringComparer.Ordinal)
                .ToList();

            var blocks = currencies.Select(currency =>
            {
                var mine = rows.Where(r => r.Currency == currency).ToList();
                var billed = mine.Where(r => r.ReceiptDate >= start && r.ReceiptDate < next).Sum(r => r.Total);
                var collected = mine.Where(r => r.Status == PaymentStatuses.Paid && r.PaidAt is { } p && p >= startUtc && p < nextUtc).Sum(r => r.Total);
                // Owed at the end of the month: issued by then, and not paid by then.
                var owed = mine.Where(r => r.ReceiptDate < next && !(r.Status == PaymentStatuses.Paid && r.PaidAt is { } p && p < nextUtc)).ToList();
                return new
                {
                    currency,
                    billed,
                    collected,
                    outstanding = owed.Sum(r => r.Total),
                    outstandingApartments = owed.Select(r => r.ApartmentId).Distinct().Count(),
                };
            }).ToList();

            return Reply.Ok(new
            {
                hasBuilding = true,
                buildingName = building.BuildingName,
                isAdmin = building.AdminUserId == user.Id,
                month = Month(start),
                earliest = Month(earliest),
                receiptsIssued = rows.Where(r => r.ReceiptDate >= start && r.ReceiptDate < next).Select(r => r.ReceiptId).Distinct().Count(),
                // A month with nothing billed, collected or owed has no block at all.
                currencies = blocks.Where(b => b.billed != 0 || b.collected != 0 || b.outstanding != 0).ToList(),
            });
        });

    /// <summary>The administrator's building, or the building of the resident's approved apartment.</summary>
    private async Task<Building?> FindBuildingAsync(User user, CancellationToken ct)
    {
        var own = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == user.Id, ct);
        if (own is not null) return own;
        return await _db.Apartments.AsNoTracking()
            .Where(a => a.UserId == user.Id && a.Status == ApartmentStatuses.Approved)
            .Select(a => a.Building)
            .FirstOrDefaultAsync(ct);
    }

    private static string Month(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static bool TryParseMonth(string? text, out DateOnly start)
    {
        start = default;
        if (!DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        if (parsed < MinDate || parsed > new DateOnly(2100, 12, 1)) return false;
        start = parsed;
        return true;
    }
}
