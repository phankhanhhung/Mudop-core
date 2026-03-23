using BMMDL.Registry.Api.Models;
using BMMDL.Registry.Data;
using BMMDL.Registry.Entities;
using BMMDL.Registry.Repositories;
using BMMDL.Registry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BMMDL.Registry.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health/live")]
    public IActionResult Live() => Ok(new { status = "Healthy", timestamp = DateTime.UtcNow });

    [HttpGet("/health/ready")]
    public IActionResult Ready() => Ok(new { status = "Ready", timestamp = DateTime.UtcNow });
}

[ApiController]
[Route("api")]
public class ApiInfoController : ControllerBase
{
    [HttpGet]
    public IActionResult GetInfo() => Ok(new
    {
        name = "BMMDL Registry API",
        version = "1.0.0",
        entities = new[] { "Tenant", "Module", "ModelPackage", "ModelElement", "Migration", "TypeDefinition", "Annotation", "ElementReference" }
    });
}

[ApiController]
[Route("api/registry/tenants")]
[Authorize(Policy = "AdminKeyPolicy")]
public class RegistryTenantsController : ControllerBase
{
    private readonly RegistryDbContext _db;
    public RegistryTenantsController(RegistryDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _db.Tenants.ToListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var e = await _db.Tenants.FindAsync(id);
        return e == null ? NotFound() : Ok(e);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Tenant? entity)
    {
        // Input validation
        if (entity == null)
        {
            return BadRequest(ODataErrorResponse.FromException("INVALID_REQUEST", "Request body is required"));
        }
        
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            return BadRequest(ODataErrorResponse.FromException("INVALID_REQUEST", "Tenant name is required"));
        }
        
        if (entity.Name.Length > 255)
        {
            return BadRequest(ODataErrorResponse.FromException("INVALID_REQUEST", "Tenant name cannot exceed 255 characters"));
        }
        
        // Check for duplicate name
        var existing = await _db.Tenants.FirstOrDefaultAsync(t => t.Name == entity.Name);
        if (existing != null)
        {
            return Conflict(ODataErrorResponse.FromException("CONFLICT", $"Tenant with name '{entity.Name}' already exists"));
        }
        
        entity.Id = Guid.NewGuid();
        entity.CreatedAt = DateTime.UtcNow;
        _db.Tenants.Add(entity);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, entity);
    }
}

[ApiController]
[Route("api/registry/modules")]
[Authorize(Policy = "AdminKeyPolicy")]
public class RegistryModulesController : ControllerBase
{
    private readonly IModuleRepository _repo;
    private readonly ObjectVersionRepository _versionRepo;
    
    public RegistryModulesController(IModuleRepository repo, ObjectVersionRepository versionRepo)
    {
        _repo = repo;
        _versionRepo = versionRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? tenantId = null)
    {
        // If no tenantId provided, return empty list (need tenant context)
        if (!tenantId.HasValue)
        {
            return Ok(new List<object>());
        }
        
        var modules = await _repo.GetByTenantAsync(tenantId.Value);
        
        // Build response with computed EntityCount
        var result = new List<object>();
        foreach (var m in modules)
        {
            var entityCount = await _versionRepo.GetEntityCountForModuleAsync(m.Id);
            result.Add(new
            {
                m.Id,
                m.Name,
                m.Version,
                m.Status,
                m.CreatedAt,
                EntityCount = entityCount
            });
        }
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var e = await _repo.GetByIdAsync(id);
        return e == null ? NotFound() : Ok(e);
    }
}

[ApiController]
[Route("api/modules")]
[Authorize(Policy = "AdminKeyPolicy")]
public class ModulesController : ControllerBase
{
    private readonly IModuleRepository _repo;
    private readonly ApprovalWorkflow _workflow;
    
    public ModulesController(IModuleRepository repo, ApprovalWorkflow workflow)
    {
        _repo = repo;
        _workflow = workflow;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid tenantId)
        => Ok(await _repo.GetByTenantAsync(tenantId));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var e = await _repo.GetByIdAsync(id);
        return e == null ? NotFound() : Ok(e);
    }

    [HttpGet("name/{name}/latest")]
    public async Task<IActionResult> GetLatest([FromQuery] Guid tenantId, string name)
    {
        var e = await _repo.GetLatestVersionAsync(tenantId, name);
        return e == null ? NotFound() : Ok(e);
    }

    // REMOVED: Manual module creation endpoint
    // Modules can ONLY be created through the BMMDL Compiler Pipeline
    // Use: dotnet run --project BMMDL.Compiler -- pipeline <file>.bmmdl -p

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(Guid id)
    {
        var result = await _workflow.SubmitModuleAsync(id);
        return result.IsSuccess ? Ok(result.Message) : BadRequest(result.Message);
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromQuery] string? approvedBy)
    {
        if (string.IsNullOrWhiteSpace(approvedBy))
        {
            return BadRequest(ODataErrorResponse.FromException("INVALID_REQUEST", "approvedBy parameter is required"));
        }
        
        var result = await _workflow.ApproveModuleAsync(id, approvedBy);
        return result.IsSuccess ? Ok(result.Message) : BadRequest(result.Message);
    }
}

// PackagesController and ElementsController REMOVED
// These controllers queried legacy model_packages and model_elements tables
// which are no longer populated by the compiler.
// Use normalized schema endpoints for browsing meta-model data.

[ApiController]
[Route("api/migrations")]
[Authorize(Policy = "AdminKeyPolicy")]
public class MigrationsController : ControllerBase
{
    private readonly IMigrationRepository _repo;
    private readonly ApprovalWorkflow _workflow;
    
    public MigrationsController(IMigrationRepository repo, ApprovalWorkflow workflow)
    {
        _repo = repo;
        _workflow = workflow;
    }

    [HttpGet]
    public async Task<IActionResult> GetByModule([FromQuery] Guid moduleId)
        => Ok(await _repo.GetByModuleAsync(moduleId));

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending([FromQuery] Guid tenantId)
        => Ok(await _repo.GetPendingAsync(tenantId));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var e = await _repo.GetByIdAsync(id);
        return e == null ? NotFound() : Ok(e);
    }

    // REMOVED: Manual migration creation endpoint
    // Migrations are auto-generated by ModelDifferService.Diff() during module publication. Manual creation bypasses ChangeSet computation

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(Guid id)
    {
        var result = await _workflow.SubmitMigrationAsync(id);
        return result.IsSuccess ? Ok(result.Message) : BadRequest(result.Message);
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromQuery] Guid approvedBy)
    {
        var result = await _workflow.ApproveMigrationAsync(id, approvedBy);
        return result.IsSuccess ? Ok(result.Message) : BadRequest(result.Message);
    }

    [HttpPost("{id}/execute")]
    public async Task<IActionResult> Execute(Guid id, [FromQuery] Guid executedBy)
    {
        var result = await _workflow.ExecuteMigrationAsync(id, executedBy);
        return result.IsSuccess ? Ok(result.Message) : BadRequest(result.Message);
    }
}

// Legacy controllers removed: TypesController, AnnotationsController, ReferencesController
// These queried legacy TypeDefinitions, Annotations, and ElementReferences entities

[ApiController]
[Route("api/dependencies")]
[Authorize(Policy = "AdminKeyPolicy")]
public class DependenciesController : ControllerBase
{
    private readonly RegistryDbContext _db;
    public DependenciesController(RegistryDbContext db) => _db = db;

    [HttpGet("module/{moduleId}")]
    public async Task<IActionResult> GetModuleDeps(Guid moduleId)
        => Ok(await _db.ModuleDependencies.Where(d => d.ModuleId == moduleId).ToListAsync());
    
    // Legacy GetPackageDeps endpoint removed (PackageDependency entity deleted)
}

/// <summary>
/// REST API for module installations.
/// </summary>
[ApiController]
[Route("api/tenants/{tenantId}/installations")]
[Authorize(Policy = "AdminKeyPolicy")]
public class InstallationsController : ControllerBase
{
    private readonly IModuleInstallationService _service;
    
    public InstallationsController(IModuleInstallationService service)
    {
        _service = service;
    }
    
    /// <summary>
    /// Get installation history for a tenant.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetHistory(Guid tenantId)
    {
        var history = await _service.GetInstallationHistoryAsync(tenantId);
        return Ok(history);
    }
    
    /// <summary>
    /// Install a published module by ID.
    /// </summary>
    [HttpPost("{moduleId}/install")]
    public async Task<IActionResult> InstallModule(
        Guid tenantId,
        Guid moduleId,
        [FromQuery] string installedBy = "system")
    {
        var result = await _service.InstallPublishedModuleAsync(tenantId, moduleId, installedBy);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }
    
    /// <summary>
    /// Uninstall a module.
    /// </summary>
    [HttpPost("{moduleId}/uninstall")]
    public async Task<IActionResult> UninstallModule(
        Guid tenantId,
        Guid moduleId,
        [FromQuery] string uninstalledBy = "system")
    {
        var result = await _service.UninstallModuleAsync(tenantId, moduleId, uninstalledBy);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }
    
    /// <summary>
    /// Check if a module can be uninstalled.
    /// </summary>
    [HttpGet("{moduleId}/can-uninstall")]
    public async Task<IActionResult> CanUninstall(Guid tenantId, Guid moduleId)
    {
        var result = await _service.CanUninstallAsync(tenantId, moduleId);
        return Ok(result);
    }
}
