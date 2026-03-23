namespace BMMDL.Runtime.Api.Middleware;

/// <summary>
/// Middleware that adds OData v4 required headers to responses.
/// - OData-Version: 4.0
/// - Content-Type: application/json;odata.metadata=minimal (for JSON responses)
/// </summary>
public class ODataHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public ODataHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if this is an OData request
        var path = context.Request.Path.Value ?? "";
        var isODataRequest = path.Contains("/api/odata", StringComparison.OrdinalIgnoreCase) ||
                             path.Contains("/$metadata", StringComparison.OrdinalIgnoreCase);

        if (isODataRequest)
        {
            // Add OData-Version header before response starts
            context.Response.OnStarting(() =>
            {
                // OData-Version is required for all OData responses
                context.Response.Headers["OData-Version"] = "4.0";

                // Set OData-specific Content-Type if returning JSON
                var contentType = context.Response.ContentType;
                if (string.IsNullOrEmpty(contentType) || contentType.StartsWith("application/json"))
                {
                    context.Response.ContentType = "application/json;odata.metadata=minimal;charset=utf-8";
                }

                return Task.CompletedTask;
            });
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for OData middleware registration.
/// </summary>
public static class ODataMiddlewareExtensions
{
    /// <summary>
    /// Add OData v4 header middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseODataHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ODataHeaderMiddleware>();
    }
}
