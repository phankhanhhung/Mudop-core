using BMMDL.Runtime.Plugins;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BMMDL.Runtime.Api.Plugins;

/// <summary>
/// Provides admin API endpoint definitions.
/// Endpoints are registered during app startup via minimal API or controller discovery.
/// </summary>
public interface IAdminApiProvider : IPlatformFeature
{
    /// <summary>
    /// Register endpoints on the given route builder.
    /// Example: endpoints.MapPost("/api/tenants", CreateTenant);
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>
    /// Register services needed by this plugin's endpoints.
    /// Example: services.AddScoped&lt;ITenantService, DynamicPlatformTenantService&gt;();
    /// </summary>
    void RegisterServices(IServiceCollection services);
}
