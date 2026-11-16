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
/// The building's invite code, which the administrator shares over WhatsApp.
///   GET  building/invite          the current code (if it still works), for the administrator
///   POST building/invite          makes a new code that works for 8 hours; the old one stops working
///   POST building/invite/revoke   stops the code
///   GET  invite/lookup?code=      which building a code is for (any signed-in person)
/// A building has one code at a time. It does not let anyone in: it names the building, and the person still sends a join
/// request (appartment/request with inviteCode) that the administrator approves.
/// </summary>
public sealed class BuildingInviteFunctions : ApiFunction
{
    private readonly BaytakDbContext _db;
    private readonly TimeProvider _time;

    public BuildingInviteFunctions(IAccessGuard guard, BaytakDbContext db, TimeProvider time, ILogger<BuildingInviteFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _time = time;
    }

    [Function("BuildingInviteGet")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "building/invite")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.BadRequest("Please set up your building first");

            var now = _time.GetUtcNow().UtcDateTime;
            var invite = await _db.BuildingInvites.AsNoTracking().FirstOrDefaultAsync(i => i.BuildingId == building.Id && i.ExpiresAt > now, ct);
            return Reply.Ok(Describe(building, invite));
        });

    [Function("BuildingInviteCreate")]
    public Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "building/invite")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.BadRequest("Please set up your building first");

            var now = _time.GetUtcNow().UtcDateTime;
            var invite = await _db.BuildingInvites.FirstOrDefaultAsync(i => i.BuildingId == building.Id, ct);
            if (invite is null)
            {
                invite = new BuildingInvite { BuildingId = building.Id };
                _db.BuildingInvites.Add(invite);
            }

            // A new code each time, so a code that went further than the group stops working. Collisions are practically
            // impossible (31^8 codes); the check is for the day they are not.
            string code;
            do code = InviteCodes.New();
            while (await _db.BuildingInvites.AnyAsync(i => i.Code == code && i.BuildingId != building.Id, ct));

            invite.Code = code;
            invite.CreatedAt = now;
            invite.ExpiresAt = now.AddHours(InviteCodes.Hours);
            await _db.SaveChangesAsync(ct);

            Logger.LogInformation("Invite code made for building {BuildingId}", building.Id);
            return Reply.Ok(Describe(building, invite));
        });

    [Function("BuildingInviteRevoke")]
    public Task<IActionResult> Revoke(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "building/invite/revoke")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.BadRequest("Please set up your building first");

            var invite = await _db.BuildingInvites.FirstOrDefaultAsync(i => i.BuildingId == building.Id, ct);
            if (invite is not null)
            {
                _db.BuildingInvites.Remove(invite);
                await _db.SaveChangesAsync(ct);
                Logger.LogInformation("Invite code stopped for building {BuildingId}", building.Id);
            }
            return Reply.Message("The invite code was stopped");
        });

    [Function("InviteLookup")]
    public Task<IActionResult> Lookup(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "invite/lookup")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async _ =>
        {
            var code = InviteCodes.Normalize(req.Query["code"].ToString());
            var now = _time.GetUtcNow().UtcDateTime;
            var invite = code is null ? null : await _db.BuildingInvites.AsNoTracking().Include(i => i.Building).FirstOrDefaultAsync(i => i.Code == code && i.ExpiresAt > now, ct);
            if (invite?.Building is not { } building) return Reply.NotFound("This invite code is not valid or has expired");

            return Reply.Ok(new
            {
                buildingId = building.Id,
                buildingName = building.BuildingName,
                street = building.Street,
                city = building.City,
                numberOfFloors = building.NumberOfFloors,
                expiresAt = invite.ExpiresAt,
            });
        });

    private static object Describe(Building building, BuildingInvite? invite) => new
    {
        active = invite is not null,
        buildingName = building.BuildingName,
        code = invite?.Code,
        expiresAt = invite?.ExpiresAt,
    };
}
