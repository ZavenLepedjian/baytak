using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Domain;
using Baytak.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Api.Functions;

public sealed class ProfileDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? EmergencyContactPersonPhoneNumber { get; set; }
    public string? EmergencyContactPersonFullName { get; set; }
}

public sealed class NotificationRequest
{
    public string? NotificationToken { get; set; }
}

/// <summary>The signed-in user's own account: approval status, profile and push token.</summary>
public sealed class UserFunctions : ApiFunction
{
    private readonly BaytakDbContext _db;
    private readonly PhoneOptions _phone;

    public UserFunctions(IAccessGuard guard, BaytakDbContext db, IOptions<PhoneOptions> phone, ILogger<UserFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _phone = phone.Value;
    }

    [Function("UserStatus")]
    public Task<IActionResult> Status(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "user/status")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, user => Task.FromResult<IActionResult>(Reply.Ok(new { userStatus = user.Status, role = user.Role })));

    [Function("UserGetProfile")]
    public Task<IActionResult> GetProfile(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "user/get/profile")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, user => Task.FromResult<IActionResult>(Reply.Ok(new
        {
            firstName = user.FirstName,
            lastName = user.LastName,
            phoneNumber = user.PhoneNumber,
            emergencyContactPersonPhoneNumber = user.EmergencyContactPhone,
            emergencyContactPersonFullName = user.EmergencyContactName,
        })));

    [Function("UserSaveProfile")]
    public Task<IActionResult> SaveProfile(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "user/save/profile")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var (body, error) = await Reply.ReadJsonAsync<ProfileDto>(req, ct);
            if (body is null) return Reply.BadRequest(error!);

            var first = Text.Clean(body.FirstName);
            var last = Text.Clean(body.LastName);
            if (first.Length == 0) return Reply.BadRequest("First name is required");
            if (last.Length == 0) return Reply.BadRequest("Last name is required");
            if (first.Length > 80 || last.Length > 80) return Reply.BadRequest("Names must be at most 80 characters");

            var emergencyName = Text.Clean(body.EmergencyContactPersonFullName);
            if (emergencyName.Length > 120) return Reply.BadRequest("Emergency contact name is too long");

            var emergencyPhone = string.Empty;
            if (!string.IsNullOrWhiteSpace(body.EmergencyContactPersonPhoneNumber))
            {
                emergencyPhone = PhoneNumber.Normalize(body.EmergencyContactPersonPhoneNumber, _phone.DefaultCountryCode) ?? string.Empty;
                if (emergencyPhone.Length == 0) return Reply.BadRequest("The emergency contact number is not valid");
            }

            user.FirstName = first;
            user.LastName = last;
            user.EmergencyContactName = emergencyName;
            user.EmergencyContactPhone = emergencyPhone;
            await _db.SaveChangesAsync(ct);
            return Reply.Message("Profile saved successfully");
        });

    [Function("UserSetupNotification")]
    public Task<IActionResult> SetupNotification(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "setup/notification")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var (body, error) = await Reply.ReadJsonAsync<NotificationRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);

            var token = body.NotificationToken?.Trim();
            if (!ExpoPushToken.IsValid(token)) return Reply.BadRequest("Invalid notification token");

            // A device belongs to one account at a time: detach the token from whoever had it before.
            await _db.Users.Where(u => u.ExpoPushToken == token && u.Id != user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpoPushToken, (string?)null), ct);
            user.ExpoPushToken = token;
            await _db.SaveChangesAsync(ct);
            return Reply.Message("Notifications enabled");
        });

    /// <summary>
    /// Server-side sign-out: revokes every token issued so far and stops push notifications to the device,
    /// so a stolen or left-behind token is useless after the user signs out.
    /// </summary>
    [Function("UserLogout")]
    public Task<IActionResult> Logout(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "user/logout")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            user.SessionVersion++;
            user.ExpoPushToken = null;
            await _db.SaveChangesAsync(ct);
            return Reply.Message("Signed out");
        });

    /// <summary>
    /// Permanently deletes the account and the personal data attached to it (profile, apartment, receipts
    /// issued to that apartment, wallet, login codes). Required by the App Store / Play policies and GDPR.
    /// An administrator whose building still has residents or receipts must hand it over first.
    /// </summary>
    [Function("UserDelete")]
    public Task<IActionResult> DeleteAccount(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "user/delete")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var building = await _db.Buildings.FirstOrDefaultAsync(b => b.AdminUserId == user.Id, ct);
            if (building is not null)
            {
                var hasResidents = await _db.Apartments.AnyAsync(a => a.BuildingId == building.Id && a.UserId != user.Id, ct);
                var hasReceipts = await _db.Receipts.AnyAsync(r => r.BuildingId == building.Id, ct);
                if (hasResidents || hasReceipts)
                    return Reply.Conflict("Your building still has residents or receipts. Please contact support to hand it over before deleting your account.");
            }

            // Removed in one SaveChanges (one transaction); EF orders the deletes by their dependencies.
            _db.Apartments.RemoveRange(await _db.Apartments.Where(a => a.UserId == user.Id).ToListAsync(ct));
            _db.WalletEntries.RemoveRange(await _db.WalletEntries.Where(w => w.UserId == user.Id).ToListAsync(ct));
            _db.OtpCodes.RemoveRange(await _db.OtpCodes.Where(o => o.PhoneNumber == user.PhoneNumber).ToListAsync(ct));
            if (building is not null)
            {
                _db.ReceiptFormats.RemoveRange(await _db.ReceiptFormats.Where(f => f.BuildingId == building.Id).ToListAsync(ct));
                _db.Buildings.Remove(building);
            }
            _db.Users.Remove(user);
            await _db.SaveChangesAsync(ct);

            Logger.LogInformation("Account {UserId} deleted at the user's request", user.Id);
            return Reply.Message("Your account has been deleted");
        });
}
