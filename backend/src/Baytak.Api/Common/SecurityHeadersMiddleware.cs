using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace Baytak.Api.Common;

/// <summary>
/// Adds standard API security headers (OWASP Secure Headers) to every HTTP response. Responses carry personal
/// data, so nothing may be cached by intermediaries, sniffed as another content type, or framed.
/// </summary>
public sealed class SecurityHeadersMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = context.GetHttpContext();
        if (http is not null)
        {
            http.Response.OnStarting(() =>
            {
                var headers = http.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Cache-Control"] = "no-store";
                headers["Pragma"] = "no-cache";
                headers["Referrer-Policy"] = "no-referrer";
                headers["X-Frame-Options"] = "DENY";
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                return Task.CompletedTask;
            });
        }
        await next(context);
    }
}
