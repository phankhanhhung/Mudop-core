using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime;

/// <summary>
/// Seeds platform data (roles, permissions, admin user).
/// </summary>
public class PlatformSeeder
{
    private readonly PlatformRuntime _runtime;
    private readonly ILogger<PlatformSeeder> _logger;
    
    // Default role IDs (fixed for consistency)
    public static readonly Guid SuperAdminRoleId = Guid.Parse("00000000-0000-0000-0001-000000000001");
    public static readonly Guid TenantAdminRoleId = Guid.Parse("00000000-0000-0000-0001-000000000002");
    public static readonly Guid UserRoleId = Guid.Parse("00000000-0000-0000-0001-000000000003");
    public static readonly Guid GuestRoleId = Guid.Parse("00000000-0000-0000-0001-000000000004");
    
    // Default admin user ID
    public static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0002-000000000001");
    
    public PlatformSeeder(PlatformRuntime runtime, ILogger<PlatformSeeder> logger)
    {
        _runtime = runtime;
        _logger = logger;
    }
    
    /// <summary>
    /// Seed all platform data.
    /// </summary>
    public async Task SeedAllAsync(string adminEmail, string adminPassword, CancellationToken ct = default)
    {
        _logger.LogInformation("Seeding platform data...");
        
        await SeedRolesAsync(ct);
        await SeedPermissionsAsync(ct);
        await SeedAdminUserAsync(adminEmail, adminPassword, ct);
        
        _logger.LogInformation("Platform data seeded successfully");
    }
    
    /// <summary>
    /// Seed default roles.
    /// </summary>
    public async Task SeedRolesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Seeding default roles...");
        
        var rolesRepo = _runtime.Roles;
        
        var roles = new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = SuperAdminRoleId,
                ["roleName"] = "SuperAdmin",
                ["roleDescription"] = "Super administrator with full platform access",
                ["isSystemRole"] = true,
                ["roleScope"] = "platform",
                ["tenantId"] = PlatformRuntime.SystemTenantId,
                ["isActive"] = true,
                ["createdAt"] = DateTime.UtcNow
            },
            new Dictionary<string, object?>
            {
                ["id"] = TenantAdminRoleId,
                ["roleName"] = "TenantAdmin",
                ["roleDescription"] = "Tenant administrator with full tenant access",
                ["isSystemRole"] = true,
                ["roleScope"] = "tenant",
                ["tenantId"] = PlatformRuntime.SystemTenantId,
                ["isActive"] = true,
                ["createdAt"] = DateTime.UtcNow
            },
            new Dictionary<string, object?>
            {
                ["id"] = UserRoleId,
                ["roleName"] = "User",
                ["roleDescription"] = "Standard user with basic access",
                ["isSystemRole"] = true,
                ["roleScope"] = "tenant",
                ["tenantId"] = PlatformRuntime.SystemTenantId,
                ["isActive"] = true,
                ["createdAt"] = DateTime.UtcNow
            },
            new Dictionary<string, object?>
            {
                ["id"] = GuestRoleId,
                ["roleName"] = "Guest",
                ["roleDescription"] = "Guest user with read-only access",
                ["isSystemRole"] = true,
                ["roleScope"] = "tenant",
                ["tenantId"] = PlatformRuntime.SystemTenantId,
                ["isActive"] = true,
                ["createdAt"] = DateTime.UtcNow
            }
        };
        
        foreach (var role in roles)
        {
            var roleId = (Guid)role["id"]!;
            var exists = await rolesRepo.ExistsAsync(roleId, ct);
            
            if (!exists)
            {
                await rolesRepo.CreateAsync(role, ct);
                _logger.LogInformation("Created role: {RoleName}", role["roleName"]);
            }
            else
            {
                _logger.LogDebug("Role already exists: {RoleName}", role["roleName"]);
            }
        }
    }
    
    /// <summary>
    /// Seed default permissions.
    /// </summary>
    public async Task SeedPermissionsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Seeding default permissions...");
        
        var permRepo = _runtime.Permissions;
        
        var permissions = new[]
        {
            // Platform management
            CreatePermission("platform:manage", "Platform", "manage", "Manage platform settings"),
            CreatePermission("platform:read", "Platform", "read", "Read platform settings"),
            
            // Tenant management
            CreatePermission("tenant:create", "Tenant", "create", "Create tenants"),
            CreatePermission("tenant:read", "Tenant", "read", "Read tenants"),
            CreatePermission("tenant:update", "Tenant", "update", "Update tenants"),
            CreatePermission("tenant:delete", "Tenant", "delete", "Delete tenants"),
            
            // User management
            CreatePermission("user:create", "User", "create", "Create users"),
            CreatePermission("user:read", "User", "read", "Read users"),
            CreatePermission("user:update", "User", "update", "Update users"),
            CreatePermission("user:delete", "User", "delete", "Delete users"),
            
            // Role management
            CreatePermission("role:create", "Role", "create", "Create roles"),
            CreatePermission("role:read", "Role", "read", "Read roles"),
            CreatePermission("role:update", "Role", "update", "Update roles"),
            CreatePermission("role:delete", "Role", "delete", "Delete roles"),
            CreatePermission("role:assign", "Role", "assign", "Assign roles to users"),
        };
        
        foreach (var perm in permissions)
        {
            var permName = (string)perm["permissionName"]!;
            var existing = await permRepo.QueryAsync(
                "permission_name = @name", 
                new Dictionary<string, object> { ["@name"] = permName }, 
                ct);
                
            if (existing.Count == 0)
            {
                await permRepo.CreateAsync(perm, ct);
                _logger.LogInformation("Created permission: {PermName}", permName);
            }
        }
    }
    
    /// <summary>
    /// Seed admin user.
    /// </summary>
    public async Task SeedAdminUserAsync(string email, string password, CancellationToken ct = default)
    {
        _logger.LogInformation("Seeding admin user...");
        
        var usersRepo = _runtime.Users;
        
        var exists = await usersRepo.ExistsAsync(AdminUserId, ct);
        if (exists)
        {
            _logger.LogInformation("Admin user already exists");
            return;
        }
        
        // Hash password (simple hash for demo - use proper hashing in production!)
        var passwordHash = HashPassword(password);
        
        var adminUser = new Dictionary<string, object?>
        {
            ["id"] = AdminUserId,
            ["username"] = "admin",
            ["email"] = email,
            ["passwordHash"] = passwordHash,
            ["firstName"] = "System",
            ["lastName"] = "Administrator",
            ["isActive"] = true,
            ["isEmailVerified"] = true,
            ["isMfaEnabled"] = false,
            ["tenantId"] = PlatformRuntime.SystemTenantId,
            ["createdAt"] = DateTime.UtcNow
        };
        
        await usersRepo.CreateAsync(adminUser, ct);
        _logger.LogInformation("Created admin user: {Email}", email);
        
        // Assign SuperAdmin role
        var assignmentRepo = _runtime.CreateRepository("UserRoleAssignment");
        var assignment = new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["userId"] = AdminUserId,
            ["roleId"] = SuperAdminRoleId,
            ["assignedAt"] = DateTime.UtcNow,
            ["assignedById"] = AdminUserId
        };
        
        await assignmentRepo.CreateAsync(assignment, ct);
        _logger.LogInformation("Assigned SuperAdmin role to admin user");
    }
    
    private Dictionary<string, object?> CreatePermission(string name, string category, string action, string desc)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["permissionName"] = name,
            ["permissionDescription"] = desc,
            ["category"] = category,
            ["permissionAction"] = action,
            ["resource"] = category,
            ["isActive"] = true,
            ["createdAt"] = DateTime.UtcNow
        };
    }
    
    private static string HashPassword(string password)
    {
        // Use BCrypt with work factor 11 (industry standard)
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
    }
}
