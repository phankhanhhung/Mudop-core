using BMMDL.Runtime.Plugins;
using Microsoft.AspNetCore.Builder;

namespace BMMDL.Runtime.Api.Plugins;

/// <summary>
/// Provides HTTP middleware that runs in the request pipeline.
/// Order controlled by <see cref="IPlatformFeature.Stage"/>.
/// Middleware providers are invoked in dependency-resolved order during app startup.
/// </summary>
public interface IMiddlewareProvider : IPlatformFeature
{
    /// <summary>
    /// Configure middleware on the application builder.
    /// Called during app startup in dependency order.
    /// </summary>
    void UseMiddleware(IApplicationBuilder app);
}
