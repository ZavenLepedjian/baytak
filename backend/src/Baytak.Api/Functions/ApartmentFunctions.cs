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

public sealed class ApartmentRequest
{
    public string? AppartmentNumber { get; set; }
    public string? AppartmentDescription { get; set; }
    public int? Floor { get; set; }
    public int? BuildingId { get; set; }
}

public sealed class ApartmentIdRequest
{
    public int? Id { get; set; }
}

/// <summary>
/// Residents ask to join a building; the building's administrator approves or rejects.
/// (The "Appartment" spelling is part of the existing public API and is kept as is.)
/// </summary>
public sealed class ApartmentFunctions : ApiFunction
{
    private readonly BaytakDbContext _db;
    private readonly IPushSender _push;
    private readonly TimeProvider _time;

    public ApartmentFunctions(IAccessGuard guard, BaytakDbContext db, IPushSender push, TimeProvider time, ILogger<ApartmentFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _push = push;
        _time = time;
    }

    [Function("ApartmentGet")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "appartment/get")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var apt = await _db.Apartments.AsNoTracking().Include(a => a.Building).FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
            if (apt is null)
            {
                return Reply.Ok(new { appartmentNumber = "", appartmentDescription = "", floor = 0, buildingId = 0, status = "", buildingName = "" });
            }
            return Reply.Ok(new
            {
                appartmentNumber = apt.ApartmentNumber,
                appartmentDescription = apt.Description,
                floor = apt.Floor,
                buildingId = apt.BuildingId,
                status = apt.Status,
                buildingName = apt.Building?.BuildingName ?? "",
            });
        });

    [Function("ApartmentRequest")]
    public Task<IActionResult> RequestJoin(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "appartment/request")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var (body, error) = await Reply.ReadJsonAsync<ApartmentRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);

            var number = Text.Clean(body.AppartmentNumber);
            var description = Text.Clean(body.AppartmentDescription);
            if (number.Length is 0 or > 60) return Reply.BadRequest("Please enter an apartment number (up to 60 characters)");
            if (description.Length is 0 or > 500) return Reply.BadRequest("Please enter an apartment description (up to 500 characters)");
            if (body.Floor is not (>= 1 and <= 200)) return Reply.BadRequest("Please set the floor your apartment is on");
            if (body.BuildingId is not > 0) return Reply.BadRequest("Please select the building you want to join");
            var buildingId = body.BuildingId.Value;

            var building = await _db.Buildings.FirstOrDefaultAsync(b => b.Id == buildingId, ct);
            if (building is null) return Reply.BadRequest("That building no longer exists");
            if (building.NumberOfFloors > 0 && body.Floor > building.NumberOfFloors)
                return Reply.BadRequest($"This building has {building.NumberOfFloors} floors");

            var taken = await _db.Apartments.AnyAsync(a => a.BuildingId == buildingId && a.ApartmentNumber == number && a.UserId != user.Id, ct);
            if (taken) return Reply.Conflict("That apartment number is already registered in this building");

            var now = _time.GetUtcNow().UtcDateTime;
            var apt = await _db.Apartments.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
            if (apt is null)
            {
                apt = new Apartment { UserId = user.Id, CreatedAt = now };
                _db.Apartments.Add(apt);
            }
            else if (apt.Status == ApartmentStatuses.Pending) return Reply.BadRequest("Your request is already waiting for approval");
            else if (apt.Status == ApartmentStatuses.Approved) return Reply.BadRequest("You already belong to a building");

            apt.BuildingId = buildingId;
            apt.ApartmentNumber = number;
            apt.Description = description;
            apt.Floor = body.Floor.Value;
            apt.Status = ApartmentStatuses.Pending;
            apt.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);

            var adminToken = await _db.Users.Where(u => u.Id == building.AdminUserId).Select(u => u.ExpoPushToken).FirstOrDefaultAsync(ct);
            await _push.SendAsync([adminToken], "New join request", $"Apartment {number} (floor {apt.Floor}) wants to join {building.BuildingName}.", ct);

            return Reply.Message("Your request was sent to the building administration");
        });

    [Function("ApartmentApprovedList")]
    public Task<IActionResult> Approved(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "appartment/approved/get")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, admin => ListAsync(admin, ApartmentStatuses.Approved, ct));

    [Function("ApartmentPendingList")]
    public Task<IActionResult> Pending(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "appartment/pending/get")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, admin => ListAsync(admin, ApartmentStatuses.Pending, ct));

    [Function("ApartmentApprove")]
    public Task<IActionResult> Approve(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "appartment/approve")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (apt, failure) = await LoadOwnedApartmentAsync(req, admin, ct);
            if (apt is null) return failure!;
            if (apt.Status == ApartmentStatuses.Rejected) return Reply.BadRequest("This request was rejected. The resident needs to send a new request.");

            if (apt.Status != ApartmentStatuses.Approved)
            {
                apt.Status = ApartmentStatuses.Approved;
                apt.UpdatedAt = _time.GetUtcNow().UtcDateTime;
                await _db.SaveChangesAsync(ct);
                await _push.SendAsync([apt.User?.ExpoPushToken], "Request approved", "You are now part of your building.", ct);
            }
            return Reply.Message("Apartment has been added to the building successfully");
        });

    [Function("ApartmentReject")]
    public Task<IActionResult> Reject(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "appartment/reject")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (apt, failure) = await LoadOwnedApartmentAsync(req, admin, ct);
            if (apt is null) return failure!;
            if (apt.Status == ApartmentStatuses.Approved) return Reply.BadRequest("This apartment is already approved. Remove it instead.");

            if (apt.Status != ApartmentStatuses.Rejected)
            {
                apt.Status = ApartmentStatuses.Rejected;
                apt.UpdatedAt = _time.GetUtcNow().UtcDateTime;
                await _db.SaveChangesAsync(ct);
                await _push.SendAsync([apt.User?.ExpoPushToken], "Request rejected", "Your join request was not approved. You can update the details and try again.", ct);
            }
            return Reply.Message("Apartment has been rejected successfully");
        });

    /// <summary>Removes an apartment (and its receipts) from the administrator's building.</summary>
    [Function("ApartmentDelete")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "appartment/delete/{id:int}")] HttpRequest req, int id, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.NotFound("Apartment not found");
            var apt = await _db.Apartments.FirstOrDefaultAsync(a => a.Id == id && a.BuildingId == building.Id, ct);
            if (apt is null) return Reply.NotFound("Apartment not found");

            _db.Apartments.Remove(apt);
            await _db.SaveChangesAsync(ct);
            return Reply.Message("Apartment removed");
        });

    // ------------------------------------------------------------------ helpers

    private async Task<IActionResult> ListAsync(User admin, string status, CancellationToken ct)
    {
        var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
        if (building is null) return Reply.Message("NoAppartments");

        var rows = await _db.Apartments.AsNoTracking()
            .Where(a => a.BuildingId == building.Id && a.Status == status)
            .Include(a => a.User)
            .OrderBy(a => a.Floor).ThenBy(a => a.ApartmentNumber)
            .ToListAsync(ct);
        if (rows.Count == 0) return Reply.Message("NoAppartments");

        return Reply.Ok(rows.Select(a => new
        {
            id = a.Id,
            fullName = a.User?.FullName ?? "",
            floor = a.Floor,
            phoneNumber = a.User?.PhoneNumber ?? "",
            emergencyContactPersonFullName = a.User?.EmergencyContactName ?? "",
            emergencyContactPersonPhoneNumber = a.User?.EmergencyContactPhone ?? "",
            appartmentNumber = a.ApartmentNumber,
            appartmentDescription = a.Description,
        }));
    }

    private async Task<(Apartment? Apartment, IActionResult? Failure)> LoadOwnedApartmentAsync(HttpRequest req, User admin, CancellationToken ct)
    {
        var (body, error) = await Reply.ReadJsonAsync<ApartmentIdRequest>(req, ct);
        if (body is null) return (null, Reply.BadRequest(error!));
        if (body.Id is not { } id) return (null, Reply.BadRequest("Apartment id is required"));

        var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
        var apt = building is null
            ? null
            : await _db.Apartments.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == id && a.BuildingId == building.Id, ct);
        return apt is null ? (null, Reply.NotFound("Apartment not found")) : (apt, null);
    }
}
