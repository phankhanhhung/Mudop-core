namespace BMMDL.Runtime.Api.Models;

/// <summary>
/// Request model for creating a new user within a tenant.
/// </summary>
public class CreateUserRequest
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

/// <summary>
/// Request model for updating an existing user.
/// </summary>
public class UpdateUserRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Response model for tenant user operations.
/// </summary>
public class TenantUserResponse
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool IsActive { get; set; }
    public List<string>? Roles { get; set; }
    public List<string>? Permissions { get; set; }
}

/// <summary>
/// Request model for assigning a role to a user.
/// </summary>
public class AssignRoleRequest
{
    public required string RoleName { get; set; }
}

/// <summary>
/// Request model for assigning a permission to a user.
/// </summary>
public class AssignPermissionRequest
{
    public required string PermissionName { get; set; }
}
