namespace BMMDL.Runtime.Api.Observability;

using System.Diagnostics;

/// <summary>
/// Middleware that records request timing metrics.
/// </summary>
public class MetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BmmdlMetrics _metrics;

    public MetricsMiddleware(RequestDelegate next, BmmdlMetrics metrics)
    {
        _next = next;
        _metrics = metrics;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            
            // Get simplified endpoint path (remove IDs for grouping)
            var path = context.Request.Path.Value ?? "/";
            var endpoint = SimplifyEndpoint(path);
            
            _metrics.RecordRequest(
                context.Request.Method,
                endpoint,
                context.Response.StatusCode,
                sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Simplify endpoint path by replacing GUIDs with {id} for metric grouping.
    /// </summary>
    private static string SimplifyEndpoint(string path)
    {
        // Replace GUIDs with {id}
        var guidPattern = new System.Text.RegularExpressions.Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return guidPattern.Replace(path, "{id}");
    }
}

/// <summary>
/// Extension methods for metrics middleware.
/// </summary>
public static class MetricsMiddlewareExtensions
{
    public static IApplicationBuilder UseMetrics(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<MetricsMiddleware>();
    }
}
