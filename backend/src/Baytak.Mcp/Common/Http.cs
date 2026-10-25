using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace Baytak.Mcp.Common;

public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ContentResult JsonBody(object value, int status = StatusCodes.Status200OK) => new()
    {
        StatusCode = status,
        Content = JsonSerializer.Serialize(value, Json),
        ContentType = "application/json; charset=utf-8",
    };

    public static ContentResult Text(int status, string message) => new()
    {
        StatusCode = status,
        Content = message,
        ContentType = "text/plain; charset=utf-8",
    };

    /// <summary>RFC 6749 error response.</summary>
    public static ContentResult OAuthError(string error, string description, int status = StatusCodes.Status400BadRequest) =>
        JsonBody(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description }, status);

    /// <summary>
    /// A sign-in page. Its CSP allows only this app, Google Fonts, and form posts to this app plus the client's redirect
    /// origin (browsers apply form-action to the redirect that follows a form post).
    /// </summary>
    public static ContentResult Html(HttpRequest req, string html, string? redirectOrigin = null, int status = StatusCodes.Status200OK)
    {
        var formAction = redirectOrigin is null ? "'self'" : "'self' " + redirectOrigin;
        req.HttpContext.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; img-src 'self'; style-src 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; " +
            $"form-action {formAction}; frame-ancestors 'none'; base-uri 'none'";
        return new ContentResult { StatusCode = status, Content = html, ContentType = "text/html; charset=utf-8" };
    }

    public static void SetHeader(HttpRequest req, string name, string value) => req.HttpContext.Response.Headers[name] = value;
}

/// <summary>Raw bytes with a content type, written directly so it needs no MVC result executor in the Functions host.</summary>
public sealed class BytesResult : IActionResult
{
    public BytesResult(byte[] bytes, string contentType)
    {
        Bytes = bytes;
        ContentType = contentType;
    }

    public byte[] Bytes { get; }
    public string ContentType { get; }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = ContentType;
        response.ContentLength = Bytes.Length;
        await response.Body.WriteAsync(Bytes);
    }
}

/// <summary>Security headers on every response (OWASP Secure Headers). Nothing personal may be cached, sniffed or framed.</summary>
public sealed class SecurityHeadersMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = context.GetHttpContext();
        http?.Response.OnStarting(() =>
        {
            var headers = http.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            if (!headers.ContainsKey("Cache-Control")) headers["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        await next(context);
    }
}
