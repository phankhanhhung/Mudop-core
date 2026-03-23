namespace BMMDL.Runtime.Api.Middleware;

/// <summary>
/// Middleware that adds standard security headers to all HTTP responses.
/// Mitigates clickjacking, MIME sniffing, and other common web vulnerabilities.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add security headers before the response is sent
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Prevent clickjacking by disallowing framing
            headers["X-Frame-Options"] = "DENY";

            // Prevent MIME type sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // Disable XSS auditor (modern browsers don't need it; can cause issues)
            headers["X-XSS-Protection"] = "0";

            // Control referrer information sent with requests
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Content Security Policy — restrict resource loading to same origin
            headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'";

            // Permissions Policy — disable sensitive browser features
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            return Task.CompletedTask;
        });

        await _next(context);
    }
}

/// <summary>
/// Extension methods for registering security headers middleware.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
