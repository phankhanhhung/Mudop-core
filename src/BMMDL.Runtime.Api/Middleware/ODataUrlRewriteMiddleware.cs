namespace BMMDL.Runtime.Api.Middleware;

using System.Text.RegularExpressions;

/// <summary>
/// OData v4 URL rewrite middleware.
/// Rewrites key-in-parentheses URLs to segment-based routing:
///   /api/odata/{module}/{entity}({guid}) → /api/odata/{module}/{entity}/{guid}
/// This ensures OData canonical URL format works with ASP.NET route templates.
/// </summary>
public class ODataUrlRewriteMiddleware
{
    private readonly RequestDelegate _next;

    // Match: /api/odata/{module}/{entity}({guid-or-key})
    // Captures: full path before parens, the key value inside parens
    private static readonly Regex KeyInParensPattern = new(
        @"^(/api/odata/[^/]+/[^/(]+)\(([^)]+)\)(.*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ODataUrlRewriteMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        var match = KeyInParensPattern.Match(path);
        if (match.Success)
        {
            var basePath = match.Groups[1].Value;
            var key = match.Groups[2].Value.Trim('\'', '"');
            var suffix = match.Groups[3].Value;

            // Rewrite: /api/odata/Module/Entity(key)/nav → /api/odata/Module/Entity/key/nav
            context.Request.Path = new PathString($"{basePath}/{key}{suffix}");
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method for registering URL rewrite middleware.
/// </summary>
public static class ODataUrlRewriteMiddlewareExtensions
{
    /// <summary>
    /// Add OData v4 URL rewrite middleware that converts key-in-parentheses to segment-based routing.
    /// </summary>
    public static IApplicationBuilder UseODataUrlRewrite(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ODataUrlRewriteMiddleware>();
    }
}
