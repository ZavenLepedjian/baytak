using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baytak.Api.Functions;

public sealed class TransferAdminRequest
{
    /// <summary>The apartment of the member who becomes the administrator.</summary>
    public int? ApartmentId { get; set; }
}

/// <summary>
/// Handing a building over.
///   POST building/transfer-admin   { apartmentId }   the administrator makes a resident of the building its administrator
/// Everything the building holds (residents, receipts, templates) stays with the building. The former administrator
/// becomes an ordinary account; they can be made administrator again only by the person who holds the role now.
/// Both people pick up their new role through auth/refresh, because the role is written into the login token.
/// </summary>
public sealed class BuildingAdminFunctions : ApiFunction
{
    private readonly BaytakDbContext _db;
    private readonly IPushSender _push;

    public BuildingAdminFunctions(IAccessGuard guard, BaytakDbContext db, IPushSender push, ILogger<BuildingAdminFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _push = push;
    }

    [Function("BuildingTransferAdmin")]
    public Task<IActionResult> Transfer(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "building/transfer-admin")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (body, error) = await Reply.ReadJsonAsync<TransferAdminRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);
            if (body.ApartmentId is not { } apartmentId) return Reply.BadRequest("Please choose who becomes the administrator");

            var building = await _db.Buildings.FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.BadRequest("Please set up your building first");

            // Only an approved resident of this very building. Anything else is "not found", whoever it belongs to.
            var apartment = await _db.Apartments.Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == apartmentId && a.BuildingId == building.Id && a.Status == ApartmentStatuses.Approved, ct);
            if (apartment?.User is not { } successor) return Reply.NotFound("Apartment not found");

            if (successor.Id == admin.Id) return Reply.BadRequest("You already manage this building");
            // The operator turned this person's administrator request down; a transfer must not undo that.
            if (successor.Status == UserStatuses.Rejected) return Reply.BadRequest("This person cannot be made administrator");
            if (await _db.Buildings.AnyAsync(b => b.AdminUserId == successor.Id, ct)) return Reply.BadRequest("This person already manages another building");

            building.AdminUserId = successor.Id;
            successor.Role = Roles.Admin;
            successor.Status = UserStatuses.Approved;
            admin.Role = Roles.User;
            await _db.SaveChangesAsync(ct);

            Logger.LogInformation("Building {BuildingId} handed from administrator {From} to {To}", building.Id, admin.Id, successor.Id);

            // Best effort: the transfer stands even when the notification cannot be sent.
            await _push.SendAsync(
                [successor.ExpoPushToken],
                "You now manage the building",
                $"{(string.IsNullOrWhiteSpace(admin.FullName) ? "The administrator" : admin.FullName)} made you the administrator of {building.BuildingName}. Open Baytak to issue receipts and follow payments.",
                ct);

            return Reply.Message("The administrator role was transferred");
        });
}
