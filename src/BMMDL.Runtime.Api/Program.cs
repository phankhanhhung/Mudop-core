using System.Text;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Authorization;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Plugins;
using BMMDL.Runtime.Api.Hubs;
using BMMDL.Runtime.Api.Observability;
using BMMDL.Runtime.Events;
using BMMDL.Runtime.Plugins;
using BMMDL.Runtime.Storage;
using Amazon.S3;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.OData;
using BMMDL.Runtime.Services;
using BMMDL.Runtime.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);


// =============================================================================
// CONFIGURATION
// =============================================================================

// JWT Configuration — deferred binding so WebApplicationFactory test overrides are visible
builder.Services.AddSingleton(sp =>
{
    var opts = new JwtOptions();
    sp.GetRequiredService<IConfiguration>().GetSection(JwtOptions.SectionName).Bind(opts);
    return opts;
});

// =============================================================================
// SERVICES
// =============================================================================

// Add controllers with JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// In-memory cache (used for login rate limiting, etc.)
builder.Services.AddMemoryCache();

// OpenAPI (built-in .NET 10)
builder.Services.AddOpenApi();

// HttpClient for $batch internal requests
builder.Services.AddHttpClient("BatchInternal", client =>
{
    // No special config - will use same base address as incoming request
});

// HttpClient for webhook delivery (per-request timeout is set via CancellationTokenSource)
builder.Services.AddHttpClient("WebhookDelivery", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30); // per-request CancellationTokenSource overrides this
});

// CORS (AllowCredentials required for SignalR WebSocket connections)
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins(
                    "http://localhost:5173",
                    "http://localhost:3000",
                    "http://localhost:5175",
                    "http://localhost:51742")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
        else
        {
            // In production, restrict to configured origins
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                    .WithHeaders("Content-Type", "Authorization", "X-Tenant-Id", "X-Admin-Key", "If-Match", "If-None-Match", "Prefer", "X-Requested-With", "Accept")
                    .AllowCredentials();
            }
            else
            {
                // Fallback: same-origin only (no CORS headers)
                policy.SetIsOriginAllowed(_ => false);
            }
        }
    }));

// SignalR for real-time notifications
builder.Services.AddSignalR();

// =============================================================================
// AUTHENTICATION & AUTHORIZATION
// =============================================================================

// JWT Options - registered above in CONFIGURATION section

// JWT Service - Singleton
builder.Services.AddSingleton<IJwtService, JwtService>();

// Password Hasher - Singleton
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

// OAuth Validator - Singleton (stateless validation, deferred binding)
builder.Services.AddSingleton(sp =>
{
    var opts = new OAuthOptions();
    sp.GetRequiredService<IConfiguration>().GetSection(OAuthOptions.SectionName).Bind(opts);
    return opts;
});
builder.Services.AddSingleton<IOAuthValidator>(sp =>
    new OAuthValidatorService(
        sp.GetRequiredService<OAuthOptions>(),
        sp.GetRequiredService<ILogger<OAuthValidatorService>>(),
        sp.GetRequiredService<IHostEnvironment>()));

// Platform Services
// NOTE: IPlatformUserService is registered via PlatformIdentityApiPlugin.RegisterServices()
// NOTE: IPlatformTenantService is registered via MultiTenancyApiPlugin.RegisterServices()
// NOTE: IUserPreferenceService is registered via UserPreferencesPlugin.RegisterServices()

// JWT Bearer Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer();

// Configure JWT bearer options lazily so WebApplicationFactory test overrides are visible
builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(sp =>
{
    var jwt = sp.GetRequiredService<JwtOptions>();
    return new PostConfigureOptions<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
});

// Admin key authorization handler (for X-Admin-Key header)
builder.Services.AddScoped<IAuthorizationHandler, AdminKeyAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminKeyPolicy", policy =>
        policy.Requirements.Add(new AdminKeyRequirement()));
});

// =============================================================================
// BMMDL RUNTIME SERVICES
// =============================================================================

// MetaModelCacheManager - Singleton (manages cache with reload capability)
// Injects PlatformFeatureRegistry for fallback entity loading when registry DB is empty
builder.Services.AddSingleton<MetaModelCacheManager>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MetaModelCacheManager>>();
    var registry = sp.GetService<PlatformFeatureRegistry>(); // nullable — may not be registered yet
    var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("BmmdlRegistry")
        ?? throw new InvalidOperationException("Connection string 'BmmdlRegistry' not found");
    return new MetaModelCacheManager(connStr, logger, registry);
});

// CsdlGenerator - Singleton (stateless CSDL XML generation)
builder.Services.AddSingleton<CsdlGenerator>();

// ExpandExpressionParser - Singleton (stateless OData $expand parser)
builder.Services.AddSingleton<ExpandExpressionParser>();

// MetaModelCache - Factory pattern to always get CURRENT cache from manager
// IMPORTANT: Use Transient so each resolution gets the latest cache after reload
// Previously was Singleton which captured stale cache reference
builder.Services.AddTransient<MetaModelCache>(sp =>
{
    var manager = sp.GetRequiredService<MetaModelCacheManager>();
    return manager.Cache;  // Always returns current cache, not stale reference
});

// IMetaModelCache - Interface registration forwarding to concrete MetaModelCache
builder.Services.AddTransient<IMetaModelCache>(sp => sp.GetRequiredService<MetaModelCache>());




// TenantConnectionFactory - Singleton
// NOTE: Uses the BmmdlRegistry connection string because registry and platform
// share the same PostgreSQL database. Infrastructure plugin tables (audit_logs,
// event_outbox, webhooks, etc.) are created in the 'platform' schema of this database.
builder.Services.AddSingleton<ITenantConnectionFactory>(sp =>
{
    var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("BmmdlRegistry")
        ?? throw new InvalidOperationException("Connection string 'BmmdlRegistry' not found");
    return new TenantConnectionFactory(connStr);
});

// Plugin Bootstrap Options — read BEFORE AddPlatformFeatures so excluded plugins
// don't have their DI services registered (no backing database tables for excluded plugins).
var bootstrapOptions = new PluginBootstrapOptions();
builder.Configuration.GetSection("Plugins:Bootstrap").Bind(bootstrapOptions);

// Platform Features — all cross-cutting behaviors (tenant isolation, soft-delete,
// temporal, audit, ETag, etc.) go through the plugin pipeline. No legacy fallbacks.
// Discovers from BMMDL.Runtime (built-in) + BMMDL.Runtime.Api (API companion plugins).
// IServiceContributor features have their services registered here, respecting bootstrap exclusions.
builder.Services.AddPlatformFeatures(b => b
    .AddBuiltIn()
    .AddFromAssembly(typeof(BMMDL.Runtime.Api.Plugins.IAdminApiProvider).Assembly),
    bootstrapOptions);

// Dynamic Plugin Loading — directory + zip support, lifecycle management, frontend manifest
// Pre-scans external plugins for IServiceContributor registrations before builder.Build().
var pluginsDir = builder.Configuration.GetValue<string>("Plugins:Directory")
                 ?? Path.Combine(AppContext.BaseDirectory, "plugins");
var watchPlugins = builder.Configuration.GetValue<bool>("Plugins:WatchDirectory", false);
builder.Services.AddDynamicPluginLoading(pluginsDir, watchPlugins, bootstrapOptions);

// Plugin API Endpoints — discovers IAdminApiProvider implementations (e.g., MultiTenancyApiPlugin)
// and registers their services + caches providers for endpoint mapping
builder.Services.AddPluginApiEndpoints(pluginsDir);

// Registry Client — HTTP client for plugin BMMDL module installation
var registryBaseUrl = builder.Configuration.GetValue<string>("Registry:BaseUrl")
                      ?? "http://localhost:51742";
var adminApiKey = builder.Configuration.GetValue<string>("AdminApiKey")
                  ?? "bmmdl-admin-key-change-me";
builder.Services.AddHttpClient<IRegistryClient, BMMDL.Runtime.Api.Services.RegistryHttpClient>(client =>
{
    client.BaseAddress = new Uri(registryBaseUrl);
    client.DefaultRequestHeaders.Add("X-Admin-Key", adminApiKey);
    client.Timeout = TimeSpan.FromMinutes(5); // Compilation can be slow for large modules
});

// DynamicSqlBuilder - Scoped (uses MetaModelCache + plugin registry)
builder.Services.AddScoped<DynamicSqlBuilder>(sp =>
{
    var cache = sp.GetRequiredService<MetaModelCache>();
    var registry = sp.GetRequiredService<PlatformFeatureRegistry>();
    var filterState = sp.GetRequiredService<IFeatureFilterState>();
    var logger = sp.GetRequiredService<ILogger<DynamicSqlBuilder>>();
    return new DynamicSqlBuilder(cache, featureRegistry: registry, filterState: filterState, logger: logger);
});

// IDynamicSqlBuilder - Interface registration forwarding to concrete DynamicSqlBuilder
builder.Services.AddScoped<IDynamicSqlBuilder>(sp => sp.GetRequiredService<DynamicSqlBuilder>());

// QueryPlanCache - Singleton
builder.Services.AddSingleton<QueryPlanCache>(sp =>
{
    var maxSize = builder.Configuration.GetValue<int>("Api:QueryPlanCacheSize", 1000);
    return new QueryPlanCache(maxSize);
});

// Unit of Work - Scoped per-request, manages connection + transaction for write operations
builder.Services.AddScoped<IUnitOfWork>(sp =>
{
    var connectionFactory = sp.GetRequiredService<ITenantConnectionFactory>();
    var eventPublisher = sp.GetRequiredService<BMMDL.Runtime.Events.IEventPublisher>();
    var logger = sp.GetRequiredService<ILogger<UnitOfWork>>();
    var httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;
    Guid? tenantId = httpContext?.GetTenantId();
    var outboxStore = sp.GetService<BMMDL.Runtime.Events.IOutboxStore>();
    return new UnitOfWork(connectionFactory, eventPublisher, logger, tenantId, outboxStore);
});

// ParameterizedQueryExecutor - Scoped (per request, uses UoW for write transaction participation)
builder.Services.AddScoped<IQueryExecutor>(sp =>
{
    var connectionFactory = sp.GetRequiredService<ITenantConnectionFactory>();
    var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
    var httpContext = sp.GetService<IHttpContextAccessor>()?.HttpContext;
    Guid? tenantId = httpContext?.GetTenantId();
    return new ParameterizedQueryExecutor(connectionFactory, tenantId, unitOfWork);
});

// HttpContextAccessor for accessing request context in services
builder.Services.AddHttpContextAccessor();

// ReferentialIntegrityService - Scoped (uses MetaModelCache, DynamicSqlBuilder, IQueryExecutor)
builder.Services.AddScoped<ReferentialIntegrityService>();

// =============================================================================
// PHASE 4: RULE ENGINE
// =============================================================================

// FunctionRegistry - Singleton (stateless built-in functions)
builder.Services.AddSingleton<BMMDL.Runtime.Expressions.FunctionRegistry>();

// RuntimeExpressionEvaluator - Scoped (per request, uses FunctionRegistry)
builder.Services.AddScoped<BMMDL.Runtime.Expressions.RuntimeExpressionEvaluator>();

// IRuntimeExpressionEvaluator - Interface registration forwarding to concrete
builder.Services.AddScoped<BMMDL.Runtime.Expressions.IRuntimeExpressionEvaluator>(sp =>
    sp.GetRequiredService<BMMDL.Runtime.Expressions.RuntimeExpressionEvaluator>());

// AggregateExpressionResolver - Scoped (per request, resolves COUNT/SUM/AVG/MIN/MAX via DB)
builder.Services.AddScoped<BMMDL.Runtime.Expressions.AggregateExpressionResolver>(sp =>
{
    var cache = sp.GetRequiredService<BMMDL.Runtime.MetaModelCache>();
    var queryExecutor = sp.GetRequiredService<IQueryExecutor>();
    var logger = sp.GetRequiredService<ILogger<BMMDL.Runtime.Expressions.AggregateExpressionResolver>>();
    return new BMMDL.Runtime.Expressions.AggregateExpressionResolver(cache, queryExecutor, logger, SchemaConstants.PlatformSchema);
});

// RuleEngine - Scoped (per request, executes business rules)
builder.Services.AddScoped<BMMDL.Runtime.Rules.IRuleEngine, BMMDL.Runtime.Rules.RuleEngine>();

// Action/Function Executors - Scoped (hybrid PL/pgSQL + C# interpretation)
builder.Services.AddScoped<BMMDL.Runtime.Services.DatabaseActionExecutor>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<BMMDL.Runtime.Services.DatabaseActionExecutor>>();
    var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("BmmdlRegistry")
        ?? throw new InvalidOperationException("Connection string 'BmmdlRegistry' not found");
    return new BMMDL.Runtime.Services.DatabaseActionExecutor(connStr, logger);
});
builder.Services.AddScoped<BMMDL.Runtime.Services.InterpretedActionExecutor>();
builder.Services.AddScoped<BMMDL.Runtime.Services.IActionExecutor, BMMDL.Runtime.Services.HybridActionExecutor>();

// DeepInsertHandler - Scoped (handles OData v4 nested entity creation)
builder.Services.AddScoped<BMMDL.Runtime.Api.Handlers.DeepInsertHandler>();

// DeepUpdateHandler - Scoped (handles OData v4 nested entity updates)
builder.Services.AddScoped<BMMDL.Runtime.Api.Handlers.DeepUpdateHandler>();

// RecursiveExpandHandler - Scoped (handles recursive $expand with $levels)
builder.Services.AddScoped<BMMDL.Runtime.Api.Handlers.RecursiveExpandHandler>();

// SequenceService - Scoped (sequence value generation)
builder.Services.AddScoped<ISequenceService, BMMDL.Runtime.Services.SequenceService>();

// Entity CRUD Services - Scoped (extracted from DynamicEntityController/BatchController)
builder.Services.AddScoped<EntityValidationService>();
builder.Services.AddScoped<IEntityValidationService>(sp => sp.GetRequiredService<EntityValidationService>());
builder.Services.AddScoped<EntityWriteService>();
builder.Services.AddScoped<IEntityWriteService>(sp => sp.GetRequiredService<EntityWriteService>());
builder.Services.AddScoped<EntityQueryService>();
builder.Services.AddScoped<IEntityQueryService>(sp => sp.GetRequiredService<EntityQueryService>());
builder.Services.AddScoped<MediaStreamService>();
builder.Services.AddScoped<IMediaStreamService>(sp => sp.GetRequiredService<MediaStreamService>());
builder.Services.AddScoped<PropertyValueService>();
builder.Services.AddScoped<IPropertyValueService>(sp => sp.GetRequiredService<PropertyValueService>());
builder.Services.AddScoped<IEntityResolver, EntityResolver>();

// =============================================================================
// PHASE 5: AUTHORIZATION & EVENTS
// =============================================================================

// PermissionChecker - Scoped (per request, evaluates access rules)
// DefaultAccessPolicy: "Allow" for development (no rules = allow), "Deny" for production (no rules = deny)
var defaultAccessPolicy = builder.Configuration.GetValue("AccessControl:DefaultPolicy", "Allow") == "Deny"
    ? BMMDL.Runtime.Authorization.DefaultAccessPolicy.Deny
    : BMMDL.Runtime.Authorization.DefaultAccessPolicy.Allow;
builder.Services.AddScoped<BMMDL.Runtime.Authorization.IPermissionChecker>(sp =>
    new BMMDL.Runtime.Authorization.PermissionChecker(
        sp.GetRequiredService<BMMDL.Runtime.MetaModelCache>(),
        sp.GetRequiredService<BMMDL.Runtime.Expressions.RuntimeExpressionEvaluator>(),
        sp.GetRequiredService<ILogger<BMMDL.Runtime.Authorization.PermissionChecker>>(),
        defaultAccessPolicy));

// FieldRestrictionApplier - Scoped (per request, masks/hides fields)
builder.Services.AddScoped<BMMDL.Runtime.Authorization.IFieldRestrictionApplier, BMMDL.Runtime.Authorization.FieldRestrictionApplier>();

// Real-time Notifier - Singleton (pushes events to SignalR clients)
builder.Services.AddSingleton<BMMDL.Runtime.Events.IRealtimeNotifier, SignalRNotifier>();

// EventPublisher - Singleton (in-memory event bus)
builder.Services.AddSingleton<BMMDL.Runtime.Events.IEventPublisher, BMMDL.Runtime.Events.EventPublisher>();

// Infrastructure services (IOutboxStore, IAuditLogStore, IWebhookStore, IReportStore,
// ICommentStore, IChangeRequestStore, IUserPreferenceService, OutboxProcessor,
// AuditLogEventHandler) are now registered by their respective plugins via IServiceContributor
// in AddPlatformFeatures(). See: EventOutboxPlugin, AuditLoggingPlugin, WebhookPlugin,
// ReportingPlugin, CollaborationPlugin, UserPreferencesPlugin.

// IBrokerAdapter — now registered by WebhookApiPlugin via IServiceContributor
// (auto-discovered from BMMDL.Runtime.Api assembly in AddPlatformFeatures)

// Outbox Processor Service - Background hosted service (polls outbox)
// Plugin-aware: dormant when EventOutbox plugin is not loaded or not enabled.
builder.Services.AddHostedService<OutboxProcessorService>();

// ServiceEventHandler - Singleton (routes events to service handlers)
builder.Services.AddSingleton<BMMDL.Runtime.Events.ServiceEventHandler>();

// Register all event handlers with event publisher (after build)
builder.Services.AddHostedService<EventHandlerRegistrationService>();

// File Storage Provider - S3 (MinIO) or Local filesystem
var storageProvider = builder.Configuration.GetValue<string>("FileStorage:Provider") ?? "Local";
if (storageProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAmazonS3>(_ =>
    {
        var config = new AmazonS3Config
        {
            ServiceURL = builder.Configuration.GetValue<string>("FileStorage:S3:ServiceUrl") ?? "http://localhost:9000",
            ForcePathStyle = true // Required for MinIO
        };
        var accessKey = builder.Configuration.GetValue<string>("FileStorage:S3:AccessKey") ?? "bmmdl";
        var secretKey = builder.Configuration.GetValue<string>("FileStorage:S3:SecretKey") ?? "bmmdl_dev_password";
        return new AmazonS3Client(accessKey, secretKey, config);
    });
    builder.Services.AddSingleton<IFileStorageProvider, S3FileStorageProvider>();
}
else
{
    builder.Services.AddSingleton<IFileStorageProvider>(sp =>
    {
        var basePath = builder.Configuration.GetValue<string>("FileStorage:BasePath")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "file-storage");
        var logger = sp.GetRequiredService<ILogger<LocalFileStorageProvider>>();
        return new LocalFileStorageProvider(basePath, logger);
    });
}

// Scheduled materialized view refresh
builder.Services.AddHostedService<MaterializedViewRefreshService>();

// =============================================================================
// PHASE 7: ASYNC OPERATIONS (OData v4)
// =============================================================================

// AsyncOperationService - Singleton (in-memory operation tracking)
builder.Services.AddSingleton<IAsyncOperationService, AsyncOperationService>();

// DeltaTokenService - Singleton (stateless token generation)
builder.Services.Configure<DeltaTokenOptions>(
    builder.Configuration.GetSection(DeltaTokenOptions.SectionName));
builder.Services.AddSingleton<IDeltaTokenService, DeltaTokenService>();

// =============================================================================
// OBSERVABILITY (OpenTelemetry + Prometheus)
// =============================================================================

// BmmdlMetrics - Singleton (central metrics definition + IEventMetrics for Runtime layer)
builder.Services.AddSingleton<BmmdlMetrics>();
builder.Services.AddSingleton<IEventMetrics>(sp => sp.GetRequiredService<BmmdlMetrics>());

// OpenTelemetry Metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()      // HTTP request metrics
            .AddRuntimeInstrumentation()         // GC, Thread pool, etc.
            .AddMeter(BmmdlMetrics.MeterName)    // Custom BMMDL metrics
            .AddPrometheusExporter();            // Expose /metrics endpoint
    });

// =============================================================================
// BUILD APP
// =============================================================================

var app = builder.Build();

// =============================================================================
// PRODUCTION SECRET VALIDATION
// =============================================================================

var isTestEnvironment = app.Environment.EnvironmentName == "Test"
    || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Test";

if (!app.Environment.IsDevelopment() && !isTestEnvironment)
{
    var missingSecrets = new List<string>();

    var jwtSecret = app.Configuration["Jwt:SecretKey"];
    if (string.IsNullOrEmpty(jwtSecret)
        || jwtSecret.StartsWith("CHANGE_THIS", StringComparison.OrdinalIgnoreCase))
    {
        missingSecrets.Add("Jwt:SecretKey — must be set to a secure 256-bit key");
    }
    else if (jwtSecret.Length < 32)
    {
        missingSecrets.Add("Jwt:SecretKey — must be at least 32 characters (256 bits)");
    }

    var prodOAuthOptions = app.Configuration.GetSection("OAuth").Get<OAuthOptions>();
    if (prodOAuthOptions?.AllowTestTokens == true)
    {
        missingSecrets.Add("OAuth:AllowTestTokens — must be false in production");
    }

    var adminEnabled = app.Configuration.GetValue("Admin:Enabled", true);
    if (adminEnabled)
    {
        var adminKey = app.Configuration["Admin:ApiKey"];
        if (string.IsNullOrEmpty(adminKey)
            || adminKey == "bmmdl-admin-key-change-me")
        {
            missingSecrets.Add("Admin:ApiKey — must be changed from the default value");
        }
    }

    var registryClientAdminKey = app.Configuration["AdminApiKey"];
    if (!string.IsNullOrEmpty(registryClientAdminKey)
        && registryClientAdminKey == "bmmdl-admin-key-change-me")
    {
        missingSecrets.Add("AdminApiKey — must be changed from the default value");
    }

    var bmmdlRegistryConn = app.Configuration.GetConnectionString("BmmdlRegistry");
    if (string.IsNullOrEmpty(bmmdlRegistryConn)
        || bmmdlRegistryConn.Contains("bmmdl_dev_password", StringComparison.OrdinalIgnoreCase))
    {
        missingSecrets.Add("ConnectionStrings:BmmdlRegistry — must not use development password");
    }

    if (missingSecrets.Count > 0)
    {
        throw new InvalidOperationException(
            "BMMDL Runtime API cannot start in non-Development mode with default/missing secrets. " +
            "Configure the following via environment variables, user secrets, or a secure config provider:\n" +
            string.Join("\n", missingSecrets.Select(s => $"  - {s}")));
    }
}

// =============================================================================
// MIDDLEWARE PIPELINE
// =============================================================================

// Exception handling must be first
app.UseExceptionMiddleware();

// HTTPS enforcement in non-development environments
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Security headers (X-Frame-Options, X-Content-Type-Options, etc.)
app.UseSecurityHeaders();

// Metrics middleware (record request timing)
app.UseMetrics();

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

// OpenAPI endpoint (built-in .NET 10)
app.MapOpenApi();

// CORS
app.UseCors();

// OData v4 headers (OData-Version: 4.0, Content-Type)
app.UseODataHeaders();

// OData v4 URL rewrite: /Entity(key) → /Entity/key
app.UseODataUrlRewrite();

// Explicit UseRouting() after URL rewrite so routing resolves against rewritten paths.
// Without this, .NET 8+ places routing implicitly at the pipeline start (before rewrite).
app.UseRouting();

// Authentication - must be before Authorization
app.UseAuthentication();

// Tenant context extraction
app.UseTenantContext();

// Plugin middleware (Phase 8: registers all IMiddlewareProvider plugins in dependency order)
app.UsePluginMiddleware();

// Authorization (Phase 4: Now with JWT enforcement)
app.UseAuthorization();
app.UseAuthorizationMiddleware();

// Map controllers
app.MapControllers();

// Map plugin API endpoints (IAdminApiProvider — e.g., tenant CRUD from MultiTenancyApiPlugin)
app.MapPluginEndpoints();

// SignalR hub for real-time notifications
app.MapHub<NotificationHub>("/hubs/notifications");

// =============================================================================
// STARTUP
// =============================================================================

app.Logger.LogInformation("BMMDL Runtime API starting on {Urls}", 
    string.Join(", ", app.Urls.DefaultIfEmpty("http://localhost:5000")));

// Bootstrap built-in plugins: ensure plugin tables exist, auto-install and enable
// All infrastructure tables (audit_logs, event_outbox, webhooks, report_templates,
// collaboration, user_preferences) are created by their respective plugins via
// IPluginLifecycle.OnInstalledAsync during bootstrap.
{
    // Re-read bootstrap options from final app config (ensures WebApplicationFactory
    // test overrides are visible — builder.Configuration reads happen too early).
    var runtimeBootstrapOptions = new PluginBootstrapOptions();
    app.Configuration.GetSection("Plugins:Bootstrap").Bind(runtimeBootstrapOptions);

    using var scope = app.Services.CreateScope();
    var pluginManager = scope.ServiceProvider.GetRequiredService<IPluginManager>();
    if (pluginManager is PluginManager pm)
    {
        // Run on thread-pool to avoid synchronization context deadlocks with WebApplicationFactory
        await Task.Run(async () =>
        {
            await pm.EnsurePluginTablesAsync();
            await pm.BootstrapBuiltInPluginsAsync(scope.ServiceProvider, runtimeBootstrapOptions);
        });
    }
    else
    {
        app.Logger.LogWarning(
            "Plugin bootstrap skipped: IPluginManager is not PluginManager (actual type: {Type}). " +
            "Infrastructure tables may not be created.",
            pluginManager.GetType().FullName);
    }
}

app.Run();

// =============================================================================
// PROGRAM CLASS FOR TESTING
// =============================================================================

/// <summary>
/// Partial class for integration test access.
/// </summary>
public partial class Program { }

