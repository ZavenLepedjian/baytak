using Baytak.Api.Auth;
using Baytak.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baytak.Api.Common;

public enum AccessLevel
{
    /// <summary>Any signed-in account.</summary>
    Signed,
    /// <summary>An administrator whose account has been approved.</summary>
    Admin,
}

public readonly record struct AccessResult(User? User, IActionResult? Failure);

public interface IAccessGuard
{
    Task<AccessResult> AuthorizeAsync(HttpRequest request, AccessLevel level, CancellationToken ct);
}

/// <summary>
/// Authenticates the bearer token and then loads the account from the database, so role and approval
/// changes take effect immediately rather than when the token expires.
/// </summary>
public sealed class AccessGuard : IAccessGuard
{
    private readonly ITokenService _tokens;
    private readonly BaytakDbContext _db;

    public AccessGuard(ITokenService tokens, BaytakDbContext db)
    {
        _tokens = tokens;
        _db = db;
    }

    public async Task<AccessResult> AuthorizeAsync(HttpRequest request, AccessLevel level, CancellationToken ct)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return new(null, Reply.Unauthorized());
        var token = header[prefix.Length..].Trim();
        if (token.Length is 0 or > 4096) return new(null, Reply.Unauthorized());

        var subject = _tokens.Validate(token);
        if (subject is not { } s) return new(null, Reply.Unauthorized());

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == s.UserId, ct);
        // Deleted account, or the token predates a sign-out: refuse it.
        if (user is null || user.SessionVersion != s.SessionVersion) return new(null, Reply.Unauthorized());

        if (level == AccessLevel.Admin && !(user.Role == Roles.Admin && user.Status == UserStatuses.Approved))
            return new(null, Reply.Forbidden("Administrator access is required."));

        return new(user, null);
    }
}

/// <summary>Base class that wraps every handler in authentication and consistent error handling.</summary>
public abstract class ApiFunction
{
    private readonly IAccessGuard _guard;
    protected ILogger Logger { get; }

    protected ApiFunction(IAccessGuard guard, ILogger logger)
    {
        _guard = guard;
        Logger = logger;
    }

    protected Task<IActionResult> Run(HttpRequest req, AccessLevel level, CancellationToken ct, Func<User, Task<IActionResult>> action) =>
        Guarded(req, async () =>
        {
            var access = await _guard.AuthorizeAsync(req, level, ct);
            return access.Failure ?? await action(access.User!);
        });

    protected Task<IActionResult> RunAnonymous(HttpRequest req, Func<Task<IActionResult>> action) => Guarded(req, action);

    private async Task<IActionResult> Guarded(HttpRequest req, Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            return Reply.Text(StatusCodes.Status499ClientClosedRequest, "Request cancelled");
        }
        catch (DbUpdateException ex) when (Reply.IsUniqueViolation(ex))
        {
            Logger.LogWarning(ex, "Unique constraint hit on {Method} {Path}", req.Method, req.Path);
            return Reply.Conflict("That already exists. Please refresh and try again.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unhandled error on {Method} {Path}", req.Method, req.Path);
            return Reply.Text(StatusCodes.Status500InternalServerError, "Something went wrong. Please try again.");
        }
    }
}
