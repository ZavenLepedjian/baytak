using System.Globalization;
using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baytak.Api.Functions;

/// <summary>
/// Personal income/expense ledger (each user only ever sees their own entries).
///   GET  wallet                  balances + transactions (?limit=100&amp;skip=0)
///   POST wallet/add              create an entry (idempotent when an id is supplied)
///   POST wallet/update/{id}      replace an entry
///   POST wallet/delete/{id}      delete an entry (idempotent)
/// </summary>
public sealed class WalletFunctions : ApiFunction
{
    public const int MaxEntriesPerUser = 5000;
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;

    private readonly BaytakDbContext _db;
    private readonly TimeProvider _time;

    public WalletFunctions(IAccessGuard guard, BaytakDbContext db, TimeProvider time, ILogger<WalletFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _time = time;
    }

    [Function("WalletGet")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "wallet")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var limit = Reply.ParseInt(req.Query["limit"], DefaultPageSize, 1, MaxPageSize);
            var skip = Reply.ParseInt(req.Query["skip"], 0, 0, int.MaxValue);

            var mine = _db.WalletEntries.AsNoTracking().Where(e => e.UserId == user.Id);

            var sums = await mine
                .GroupBy(e => e.Currency)
                .Select(g => new { Currency = g.Key, Minor = g.Sum(e => e.Type == "income" ? e.AmountMinor : -e.AmountMinor) })
                .ToListAsync(ct);
            var balances = Money.Currencies.ToDictionary(c => c, c => (sums.FirstOrDefault(s => s.Currency == c)?.Minor ?? 0) / 100m);

            var count = await mine.CountAsync(ct);
            var page = await mine
                .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ThenBy(e => e.Id)
                .Skip(skip).Take(limit)
                .ToListAsync(ct);

            return Reply.Ok(new
            {
                balances,
                count,
                hasMore = skip + page.Count < count,
                transactions = page.Select(ToDto),
            });
        });

    [Function("WalletAdd")]
    public Task<IActionResult> Add(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "wallet/add")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var (input, error) = await ReadInput(req, ct);
            if (input is null) return Reply.BadRequest(error!);

            var id = input.Id ?? Guid.NewGuid();

            // Retrying a request whose response was lost must not create a duplicate.
            var existing = await _db.WalletEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
            if (existing is not null)
                return existing.UserId == user.Id ? Reply.Ok(ToDto(existing)) : Reply.Conflict("That id is already in use");

            if (await _db.WalletEntries.CountAsync(e => e.UserId == user.Id, ct) >= MaxEntriesPerUser)
                return Reply.BadRequest($"Wallet is full ({MaxEntriesPerUser} entries). Delete some entries first.");

            var now = _time.GetUtcNow().UtcDateTime;
            var entry = new WalletEntry
            {
                Id = id,
                UserId = user.Id,
                Description = input.Description,
                AmountMinor = input.AmountMinor,
                Currency = input.Currency,
                Type = input.Type,
                Date = input.Date,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.WalletEntries.Add(entry);
            await _db.SaveChangesAsync(ct);
            return Reply.Ok(ToDto(entry), StatusCodes.Status201Created);
        });

    [Function("WalletUpdate")]
    public Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "wallet/update/{id}")] HttpRequest req, string id, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            if (!Guid.TryParse(id, out var entryId)) return Reply.BadRequest("Invalid entry id");

            var (input, error) = await ReadInput(req, ct);
            if (input is null) return Reply.BadRequest(error!);
            if (input.Id is not null && input.Id != entryId) return Reply.BadRequest("Entry id does not match the URL");

            var entry = await _db.WalletEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == user.Id, ct);
            if (entry is null) return Reply.NotFound("Entry not found");

            entry.Description = input.Description;
            entry.AmountMinor = input.AmountMinor;
            entry.Currency = input.Currency;
            entry.Type = input.Type;
            entry.Date = input.Date;
            entry.UpdatedAt = _time.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
            return Reply.Ok(ToDto(entry));
        });

    [Function("WalletDelete")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "wallet/delete/{id}")] HttpRequest req, string id, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            if (!Guid.TryParse(id, out var entryId)) return Reply.BadRequest("Invalid entry id");
            var removed = await _db.WalletEntries.Where(e => e.Id == entryId && e.UserId == user.Id).ExecuteDeleteAsync(ct);
            // Deleting something that is already gone is a success, so client retries are harmless.
            return Reply.Ok(new { id = entryId.ToString("N"), deleted = removed > 0 });
        });

    private async Task<(EntryInput? Input, string? Error)> ReadInput(HttpRequest req, CancellationToken ct)
    {
        var (body, error) = await Reply.ReadJsonAsync<EntryRequest>(req, ct);
        if (body is null) return (null, error);
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var validation = EntryValidator.Validate(body, today);
        return validation.IsValid ? (validation.Value, null) : (null, validation.Error);
    }

    private static object ToDto(WalletEntry e) => new
    {
        id = e.Id.ToString("N"),
        description = e.Description,
        amount = e.Amount,
        currency = e.Currency,
        type = e.Type,
        date = e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        createdAt = e.CreatedAt,
    };
}
