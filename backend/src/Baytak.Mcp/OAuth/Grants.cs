using System.Buffers.Text;
using System.Text.Json;

namespace Baytak.Mcp.OAuth;

/// <summary>Purposes bound into sealed values (see <see cref="Security.Sealer"/>).</summary>
public static class Purpose
{
    public const string Client = "client";
    public const string SignIn = "signin";
    public const string Code = "code";
    public const string Access = "access";
    public const string Refresh = "refresh";
}

/// <summary>A client registered through dynamic client registration; its client_id is this record, sealed.</summary>
public sealed record ClientRecord(string Name, string[] RedirectUris);

/// <summary>The validated /oauth/authorize request, carried through the sign-in pages.</summary>
public sealed record AuthRequest(
    string ClientId, string ClientName, string RedirectUri, string? State, string CodeChallenge, string Scope, string Resource);

/// <summary>Sign-in progress: the phone once a code was sent, and the API session once it was verified.</summary>
public sealed record SignInState(AuthRequest Request, long Exp, string? Phone = null, string? Jwt = null, string? Role = null, string? Status = null);

public sealed record CodeGrant(string Jti, string ClientId, string RedirectUri, string CodeChallenge, string Scope, string Resource, string Jwt, long Exp);

public sealed record AccessGrant(string ClientId, string Scope, string Resource, string Jwt, long Exp);

public sealed record RefreshGrant(string Jti, string Family, string ClientId, string Scope, string Resource, string Jwt, long Exp);

public static class ApiJwt
{
    /// <summary>
    /// Expiry of a Baytak API token, read without validation (the API validates it on every call). Grants never outlive
    /// the API session they wrap, so a ChatGPT connection lasts at most as long as an app sign-in.
    /// </summary>
    public static long? ExpiresAt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var value) ? value : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}
