using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Baytak.Api.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Baytak.Api.Auth;

public sealed class JwtOptions
{
    /// <summary>HMAC signing secret (at least 32 characters). Keep it in app settings / Key Vault, never in source.</summary>
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "baytak";
    public string Audience { get; set; } = "baytak-app";
    /// <summary>How long a login stays valid.</summary>
    public int ExpiryDays { get; set; } = 30;
}

/// <summary>Who a valid token belongs to, and which session generation it was issued for.</summary>
public readonly record struct TokenSubject(Guid UserId, int SessionVersion);

public interface ITokenService
{
    string Issue(User user);

    /// <summary>Validates signature, issuer, audience and expiry. Returns null when invalid.</summary>
    TokenSubject? Validate(string token);
}

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;
    private readonly SigningCredentials _credentials;
    private readonly TokenValidationParameters _validation;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public TokenService(IOptions<JwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
        if (string.IsNullOrWhiteSpace(_options.Key) || Encoding.UTF8.GetByteCount(_options.Key) < 32)
            throw new InvalidOperationException("Jwt:Key must be set to a secret of at least 32 characters.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _validation = new TokenValidationParameters
        {
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Use the injected clock so expiry is testable.
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = _time.GetUtcNow().UtcDateTime;
                var skew = TimeSpan.FromSeconds(30);
                return expires is not null && expires.Value + skew > now && (notBefore is null || notBefore.Value - skew <= now);
            },
        };
    }

    /// <summary>
    /// Claims match what the mobile app reads: <c>phoneNumber</c> and <c>role</c> (plus <c>exp</c>).
    /// </summary>
    public string Issue(User user)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("phoneNumber", user.PhoneNumber),
                new Claim("role", user.Role),
                // Session generation: bumping User.SessionVersion (sign-out, account changes) revokes every older token.
                new Claim("sv", user.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ],
            notBefore: now,
            expires: now.AddDays(_options.ExpiryDays),
            signingCredentials: _credentials);
        return _handler.WriteToken(token);
    }

    public TokenSubject? Validate(string token)
    {
        try
        {
            var principal = _handler.ValidateToken(token, _validation, out _);
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var sv = principal.FindFirst("sv")?.Value;
            if (!Guid.TryParse(sub, out var id)) return null;
            if (!int.TryParse(sv, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version)) return null;
            return new TokenSubject(id, version);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}
