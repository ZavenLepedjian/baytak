using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Baytak.Api.Common;

/// <summary>
/// Response and request helpers shared by every endpoint. The mobile app expects errors as plain text
/// (it shows the body to the user) and successes as JSON (objects, arrays or a JSON string message).
/// </summary>
public static class Reply
{
    public const long MaxBodyBytes = 64 * 1024;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // Mobile clients may send numbers as strings ("12.50").
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static ContentResult Text(int status, string message) => new()
    {
        StatusCode = status,
        Content = message,
        ContentType = "text/plain; charset=utf-8",
    };

    public static ContentResult BadRequest(string message) => Text(StatusCodes.Status400BadRequest, message);
    public static ContentResult NotFound(string message) => Text(StatusCodes.Status404NotFound, message);
    public static ContentResult Conflict(string message) => Text(StatusCodes.Status409Conflict, message);
    public static ContentResult Forbidden(string message) => Text(StatusCodes.Status403Forbidden, message);

    public static ContentResult Unauthorized() => Text(StatusCodes.Status401Unauthorized, "Unauthorized");

    /// <summary>A JSON response, serialised with the API-wide options (camelCase).</summary>
    public static ContentResult Ok(object? value, int status = StatusCodes.Status200OK) => new()
    {
        StatusCode = status,
        Content = JsonSerializer.Serialize(value, Json),
        ContentType = "application/json; charset=utf-8",
    };

    /// <summary>A 200 whose body is a JSON string: a human readable message the app shows as a toast.</summary>
    public static ContentResult Message(string message) => Ok(message);

    /// <summary>Reads a JSON request body. Returns an error message instead of throwing on bad input.</summary>
    public static async Task<(T? Body, string? Error)> ReadJsonAsync<T>(HttpRequest req, CancellationToken ct) where T : class
    {
        if (req.ContentLength is > MaxBodyBytes) return (null, "Request is too large");
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(req.Body, Json, ct);
            return body is null ? (null, "Request body is required") : (body, null);
        }
        catch (JsonException)
        {
            return (null, "Request body must be valid JSON");
        }
    }

    public static bool IsUniqueViolation(DbUpdateException ex)
    {
        var text = ex.InnerException?.Message ?? ex.Message;
        return text.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) // SQLite
            || text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)       // SQL Server
            || text.Contains("Cannot insert duplicate", StringComparison.OrdinalIgnoreCase);
    }

    public static int ParseInt(string? raw, int fallback, int min, int max) =>
        int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, min, max)
            : fallback;
}
