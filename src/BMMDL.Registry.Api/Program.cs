using BMMDL.Registry.Api.Authorization;
using BMMDL.Registry.Api.Services;
using BMMDL.Registry.Data;
using BMMDL.Registry.Repositories;
using BMMDL.Registry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// ============================================================
// DATABASE CONFIGURATION
// ============================================================

// Registry Database (modules, entities, meta-model)
// Connection string resolved lazily so WebApplicationFactory test overrides are visible
builder.Services.AddDbContext<RegistryDbContext>((sp, options) =>
{
    var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connStr))
    {
        connStr = $"Host={Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost"};" +
                  $"Port={Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"};" +
                  $"Database={Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bmmdl_registry"};" +
                  $"Username={Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "bmmdl"};" +
                  $"Password={Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "bmmdl_dev_password"}";
    }
    options.UseNpgsql(connStr)
           .ConfigureWarnings(warnings =>
               warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// ============================================================
// SERVICES REGISTRATION
// ============================================================

// Repositories
builder.Services.AddScoped<IModuleRepository, ModuleRepository>();
builder.Services.AddScoped<IMigrationRepository, MigrationRepository>();
builder.Services.AddScoped<IModuleInstallationRepository, ModuleInstallationRepository>();
// Note: EfCoreMetaModelRepository requires runtime tenantId - create inline where needed

// Services
builder.Services.AddScoped<DependencyResolver>();
builder.Services.AddScoped<MigrationExecutor>();
builder.Services.AddScoped<ApprovalWorkflow>(sp =>
{
    var moduleRepo = sp.GetRequiredService<IModuleRepository>();
    var migrationRepo = sp.GetRequiredService<IMigrationRepository>();
    var depResolver = sp.GetRequiredService<DependencyResolver>();
    var migrationExecutor = sp.GetRequiredService<MigrationExecutor>();
    var config = sp.GetRequiredService<IConfiguration>();
    var platformConnStr = config.GetConnectionString("DefaultConnection")
                          ?? Environment.GetEnvironmentVariable("PLATFORM_CONNECTION_STRING");
    return new ApprovalWorkflow(moduleRepo, migrationRepo, depResolver, migrationExecutor, platformConnStr);
});
builder.Services.AddScoped<IModuleInstallationService, ModuleInstallationService>();
builder.Services.AddScoped<ModuleDiscoveryService>();
builder.Services.AddScoped<ISchemaManagementService, SchemaManagementService>();
builder.Services.AddHttpClient<IModuleCompilationService, ModuleCompilationService>();
builder.Services.AddHttpClient<IAiService, AiService>();
builder.Services.AddScoped<IAdminService, AdminService>();

// Versioning Services (Phase 1)
builder.Services.AddScoped<ObjectVersionRepository>();
builder.Services.AddScoped<VersioningService>();

// Dual-Version Sync Services (Phase 2)
builder.Services.AddScoped<DualVersionSyncService>();
builder.Services.AddScoped<VersionAwareRouter>();

// ============================================================
// AUTHENTICATION & AUTHORIZATION
// ============================================================

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var secretKey = builder.Configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(secretKey))
    };
});

// Admin key authorization
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthorizationHandler, AdminKeyAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminKeyPolicy", policy =>
        policy.Requirements.Add(new AdminKeyRequirement()));
});

// ============================================================
// API CONFIGURATION
// ============================================================

// API
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = true;
    });
builder.Services.AddEndpointsApiExplorer();

// OpenAPI (skip in test environment)
var isTestEnvironment = builder.Environment.EnvironmentName == "Test" 
    || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Test";

if (!isTestEnvironment)
{
    builder.Services.AddOpenApi();
}

// CORS
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            // In production, restrict to configured origins
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
            }
            else
            {
                // Fallback: same-origin only (no CORS headers)
                policy.SetIsOriginAllowed(_ => false);
            }
        }
    }));

var app = builder.Build();

// =============================================================================
// PRODUCTION SECRET VALIDATION
// =============================================================================

if (!app.Environment.IsDevelopment() && !isTestEnvironment)
{
    var missingSecrets = new List<string>();

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

    var allowTestTokens = app.Configuration.GetValue<bool>("OAuth:AllowTestTokens");
    if (allowTestTokens)
    {
        missingSecrets.Add("OAuth:AllowTestTokens — must be false in production");
    }

    var dbConnStr = app.Configuration.GetConnectionString("DefaultConnection") ?? "";
    if (dbConnStr.Contains("bmmdl_dev_password", StringComparison.OrdinalIgnoreCase))
    {
        missingSecrets.Add("Database password — must not use development default (bmmdl_dev_password)");
    }

    if (missingSecrets.Count > 0)
    {
        throw new InvalidOperationException(
            "BMMDL Registry API cannot start in non-Development mode with default/missing secrets. " +
            "Configure the following via environment variables, user secrets, or a secure config provider:\n" +
            string.Join("\n", missingSecrets.Select(s => $"  - {s}")));
    }
}

// OpenAPI endpoint (only in non-test environment)
if (!isTestEnvironment)
{
    app.MapOpenApi();
}

// Serve landing page
app.UseDefaultFiles();
app.UseStaticFiles();

// Middleware
app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ============================================================
// STARTUP
// ============================================================
try
{
    using var scope = app.Services.CreateScope();
    
    // Apply registry migrations
    var registryDb = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
    await registryDb.Database.MigrateAsync();
    Log.Information("Registry database migrations applied successfully");
}
catch (Exception ex)
{
    Log.Warning(ex, "Database migration warning: {Message}", ex.Message);
    // Continue startup even if migrations fail (database might be unavailable)
}

Log.Information("BMMDL Registry API starting on port 8080");
await app.RunAsync();
