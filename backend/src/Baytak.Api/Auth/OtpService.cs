using System.Security.Cryptography;
using System.Text;
using Baytak.Api.Auth.Sms;
using Baytak.Api.Data;
using Baytak.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Api.Auth;

public sealed class OtpOptions
{
    public int CodeLength { get; set; } = 6;
    public int TtlMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 30;
    public int MaxPerDay { get; set; } = 10;
    /// <summary>
    /// Fixed-code test accounts never send an SMS, so the 30 second wait and the 10 a day cap protect nothing; they only
    /// lock a reviewer or tester out right after signing out. A short pause and a larger daily cap remain so the fixed
    /// code (5 guesses per request) cannot be guessed by hammering the endpoint.
    /// </summary>
    public int FixedCodeResendCooldownSeconds { get; set; } = 2;
    public int FixedCodeMaxPerDay { get; set; } = 200;
    /// <summary>
    /// Optional fixed code for specific test accounts (e.g. app-store review). Only honoured for numbers in
    /// <see cref="FixedCodePhones"/>; those numbers never receive an SMS. Leave empty in production.
    /// </summary>
    public string FixedCode { get; set; } = string.Empty;
    /// <summary>Comma separated, already normalised numbers.</summary>
    public string FixedCodePhones { get; set; } = string.Empty;
}

public enum OtpRequestOutcome { Sent, TooSoon, DailyLimit, SmsFailed, CountryNotAllowed }

public enum OtpVerifyOutcome { Ok, Invalid, Expired, TooManyAttempts }

public readonly record struct OtpVerifyResult(OtpVerifyOutcome Outcome, string RequestedStatus = "", double? Latitude = null, double? Longitude = null);

public interface IOtpService
{
    Task<OtpRequestOutcome> RequestAsync(string phone, string requestedStatus, double? lat, double? lon, CancellationToken ct);
    Task<OtpVerifyResult> VerifyAsync(string phone, string code, CancellationToken ct);
}

public sealed class OtpService : IOtpService
{
    /// <summary>Stored instead of a code hash when the code lives with the external verifier. No code can hash to it.</summary>
    private const string ExternalCodeMarker = "external-verifier";

    private readonly BaytakDbContext _db;
    private readonly ISmsSender _sms;
    private readonly IExternalVerifier? _verifier;
    private readonly OtpOptions _options;
    private readonly SmsOptions _smsOptions;
    private readonly byte[] _pepper;
    private readonly TimeProvider _time;
    private readonly ILogger<OtpService> _logger;

    public OtpService(BaytakDbContext db, ISmsSender sms, IOptions<OtpOptions> options, IOptions<SmsOptions> smsOptions, IOptions<JwtOptions> jwt,
        TimeProvider time, ILogger<OtpService> logger, IExternalVerifier? verifier = null)
    {
        _db = db;
        _sms = sms;
        _verifier = verifier;
        _options = options.Value;
        _smsOptions = smsOptions.Value;
        _pepper = Encoding.UTF8.GetBytes(jwt.Value.Key);
        _time = time;
        _logger = logger;
    }

    public async Task<OtpRequestOutcome> RequestAsync(string phone, string requestedStatus, double? lat, double? lon, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var useFixed = IsFixedCodeAccount(phone);
        if (!useFixed && !_smsOptions.IsAllowedDestination(phone)) return OtpRequestOutcome.CountryNotAllowed;

        var recent = await _db.OtpCodes
            .Where(o => o.PhoneNumber == phone && o.CreatedAt > now.AddDays(-1))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        var cooldown = useFixed ? _options.FixedCodeResendCooldownSeconds : _options.ResendCooldownSeconds;
        var maxPerDay = useFixed ? _options.FixedCodeMaxPerDay : _options.MaxPerDay;
        if (recent.Count > 0 && (now - recent[0].CreatedAt).TotalSeconds < cooldown) return OtpRequestOutcome.TooSoon;
        if (recent.Count >= maxPerDay) return OtpRequestOutcome.DailyLimit;

        // The status is taken only from this request. It is never inherited from an earlier code: auth/login is
        // anonymous, so an earlier code may have been requested by someone else ("request-admin" planted on a
        // victim's number). The app re-sends the status itself when it asks for a new code.
        var status = requestedStatus;

        foreach (var open in recent.Where(o => o.ConsumedAt is null)) open.ConsumedAt = now;

        // Data minimisation: codes older than two days are useless (the rate-limit window is one day).
        await _db.OtpCodes.Where(o => o.CreatedAt < now.AddDays(-2)).ExecuteDeleteAsync(ct);

        // With an external verifier (Twilio Verify) the code is made and checked there; test numbers keep their fixed code.
        var external = _verifier is not null && !useFixed;
        var code = useFixed ? _options.FixedCode : external ? "" : GenerateCode(_options.CodeLength);
        var record = new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            CodeHash = external ? ExternalCodeMarker : Hash(phone, code),
            RequestedStatus = status,
            Latitude = lat,
            Longitude = lon,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.TtlMinutes),
        };
        _db.OtpCodes.Add(record);
        await _db.SaveChangesAsync(ct);

        if (!useFixed)
        {
            try
            {
                if (external) await _verifier!.StartAsync(phone, ct);
                else await _sms.SendAsync(phone, $"Your Baytak verification code is {code}. It expires in {_options.TtlMinutes} minutes.", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not send verification SMS to {Phone}", PhoneNumber.Mask(phone));
                // Don't leave a code the user can't have received; also don't count it against their limits.
                _db.OtpCodes.Remove(record);
                await _db.SaveChangesAsync(ct);
                return OtpRequestOutcome.SmsFailed;
            }
        }
        return OtpRequestOutcome.Sent;
    }

    public async Task<OtpVerifyResult> VerifyAsync(string phone, string code, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var otp = await _db.OtpCodes
            .Where(o => o.PhoneNumber == phone && o.ConsumedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otp is null) return new OtpVerifyResult(OtpVerifyOutcome.Invalid);
        if (otp.ExpiresAt <= now)
        {
            otp.ConsumedAt = now;
            await _db.SaveChangesAsync(ct);
            return new OtpVerifyResult(OtpVerifyOutcome.Expired);
        }

        otp.Attempts++;
        if (otp.Attempts > _options.MaxAttempts)
        {
            otp.ConsumedAt = now;
            await _db.SaveChangesAsync(ct);
            return new OtpVerifyResult(OtpVerifyOutcome.TooManyAttempts);
        }

        bool correct;
        if (otp.CodeHash == ExternalCodeMarker)
        {
            // Count the attempt before asking the provider, so a failure there cannot be used to try codes for free.
            await _db.SaveChangesAsync(ct);
            var digits = code.Trim();
            correct = _verifier is not null && digits.Length is >= 4 and <= 10 && digits.All(char.IsAsciiDigit)
                      && await _verifier.CheckAsync(phone, digits, ct);
        }
        else
        {
            var expected = Encoding.ASCII.GetBytes(otp.CodeHash);
            var actual = Encoding.ASCII.GetBytes(Hash(phone, code.Trim()));
            correct = CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        if (!correct)
        {
            await _db.SaveChangesAsync(ct);
            return new OtpVerifyResult(OtpVerifyOutcome.Invalid);
        }

        otp.ConsumedAt = now;
        await _db.SaveChangesAsync(ct);
        return new OtpVerifyResult(OtpVerifyOutcome.Ok, otp.RequestedStatus, otp.Latitude, otp.Longitude);
    }

    private bool IsFixedCodeAccount(string phone) =>
        !string.IsNullOrEmpty(_options.FixedCode) &&
        _options.FixedCodePhones.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(phone);

    private string Hash(string phone, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes($"{phone}:{code}")));

    private static string GenerateCode(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(chars);
    }
}
