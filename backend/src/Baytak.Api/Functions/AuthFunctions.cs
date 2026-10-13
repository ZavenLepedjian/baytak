using System.Globalization;
using System.Text.Json;
using Baytak.Api.Auth;
using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Api.Functions;

public sealed class PhoneOptions
{
    /// <summary>Prepended to numbers typed without a country code, e.g. "+961".</summary>
    public string DefaultCountryCode { get; set; } = "+961";
}

public sealed class LoginRequest
{
    public string? PhoneNumber { get; set; }
    /// <summary>"" for a normal login, "request-admin" to ask for administrator access.</summary>
    public string? Status { get; set; }
    public JsonElement? Long { get; set; }
    public JsonElement? Lat { get; set; }
}

public sealed class ValidateRequest
{
    public string? PhoneNumber { get; set; }
    public string? Pincode { get; set; }
}

/// <summary>Passwordless phone login: request an SMS code, then exchange it for a JWT. Both endpoints are anonymous.</summary>
public sealed class AuthFunctions : ApiFunction
{
    public const string RequestAdmin = "request-admin";

    private readonly BaytakDbContext _db;
    private readonly IOtpService _otp;
    private readonly ITokenService _tokens;
    private readonly PhoneOptions _phone;
    private readonly TimeProvider _time;

    public AuthFunctions(
        IAccessGuard guard, BaytakDbContext db, IOtpService otp, ITokenService tokens,
        IOptions<PhoneOptions> phone, TimeProvider time, ILogger<AuthFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _otp = otp;
        _tokens = tokens;
        _phone = phone.Value;
        _time = time;
    }

    [Function("AuthLogin")]
    public Task<IActionResult> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequest req, CancellationToken ct) =>
        RunAnonymous(req, async () =>
        {
            var (body, error) = await Reply.ReadJsonAsync<LoginRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);

            var phone = PhoneNumber.Normalize(body.PhoneNumber, _phone.DefaultCountryCode);
            if (phone is null) return Reply.BadRequest("Please enter a valid phone number");

            var status = string.Equals(body.Status?.Trim(), RequestAdmin, StringComparison.OrdinalIgnoreCase) ? RequestAdmin : string.Empty;

            var outcome = await _otp.RequestAsync(phone, status, ToDouble(body.Lat), ToDouble(body.Long), ct);
            return outcome switch
            {
                OtpRequestOutcome.Sent => Reply.Message("Verification code sent"),
                OtpRequestOutcome.TooSoon => Reply.Text(StatusCodes.Status429TooManyRequests, "Please wait a moment before requesting another code."),
                OtpRequestOutcome.CountryNotAllowed => Reply.BadRequest("Phone numbers from this country are not supported yet."),
                OtpRequestOutcome.DailyLimit => Reply.Text(StatusCodes.Status429TooManyRequests, "Too many codes requested today. Please try again tomorrow."),
                _ => Reply.Text(StatusCodes.Status500InternalServerError, "We could not send the verification code. Please try again."),
            };
        });

    [Function("AuthValidate")]
    public Task<IActionResult> Validate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/validate")] HttpRequest req, CancellationToken ct) =>
        RunAnonymous(req, async () =>
        {
            var (body, error) = await Reply.ReadJsonAsync<ValidateRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);

            var phone = PhoneNumber.Normalize(body.PhoneNumber, _phone.DefaultCountryCode);
            var code = body.Pincode?.Trim();
            if (phone is null || string.IsNullOrEmpty(code) || code.Length > 12) return Reply.BadRequest("Please enter the verification code");

            var result = await _otp.VerifyAsync(phone, code, ct);
            switch (result.Outcome)
            {
                case OtpVerifyOutcome.Expired: return Reply.BadRequest("The code has expired. Please request a new one.");
                case OtpVerifyOutcome.TooManyAttempts: return Reply.Text(StatusCodes.Status429TooManyRequests, "Too many attempts. Please request a new code.");
                case OtpVerifyOutcome.Invalid: return Reply.BadRequest("That code is not correct.");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);
            if (user is null)
            {
                user = new User { Id = Guid.NewGuid(), PhoneNumber = phone, Role = Roles.User, Status = UserStatuses.Approved, CreatedAt = now };
                _db.Users.Add(user);
            }

            // Asking for administrator access promotes the account but keeps it Pending until someone approves it.
            if (result.RequestedStatus == RequestAdmin && user.Role != Roles.Admin)
            {
                user.Role = Roles.Admin;
                user.Status = UserStatuses.Pending;
            }
            user.LastLoginAt = now;
            if (result.Latitude is { } lat && result.Longitude is { } lon)
            {
                user.LastLatitude = lat;
                user.LastLongitude = lon;
            }
            await _db.SaveChangesAsync(ct);

            return Reply.Ok(new { auth = _tokens.Issue(user), userStatus = user.Status });
        });

    private static double? ToDouble(JsonElement? value)
    {
        if (value is not { } v) return null;
        double d;
        var ok = v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDouble(out d),
            JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d),
            _ => (d = 0) != 0,
        };
        return ok && double.IsFinite(d) ? d : null;
    }
}
