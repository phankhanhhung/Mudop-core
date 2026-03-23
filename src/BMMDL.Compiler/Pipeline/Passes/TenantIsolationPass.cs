using BMMDL.Compiler.Pipeline;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Service;
using BMMDL.MetaModel.Structure;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Compiler pass for validating tenant isolation configuration.
/// Order: 48 (after BindingPass 47, before SemanticValidationPass 50)
/// </summary>
public class TenantIsolationPass : ValidationPassBase
{
    public override string Name => "Tenant Isolation Validation";
    public override string Description => "Validates tenant isolation configuration and cross-tenant reference rules";
    public override int Order => 48;

    public TenantIsolationPass(ILogger? logger = null) : base(logger) { }

    protected override bool ExecuteValidation(CompilationContext context)
    {
        bool hasErrors = false;

        // Validation 1: Check tenant-scoped entities have tenantId field
        hasErrors |= !ValidateTenantScopedEntities(context);

        // Validation 2: Check cross-tenant references
        hasErrors |= !ValidateCrossTenantReferences(context);

        // Validation 3: Check access control scope compatibility
        hasErrors |= !ValidateAccessControlScopes(context);

        // Validation 4: Module consistency warnings
        ValidateModuleConsistency(context);

        // Validation 5: Service tenant scoping warnings
        ValidateServiceTenantScoping(context);

        return !hasErrors;
    }

    /// <summary>
    /// Validation 1: Tenant-scoped entities must have tenantId field (from TenantAware aspect).
    /// </summary>
    private bool ValidateTenantScopedEntities(CompilationContext context)
    {
        if (context.Model == null) return true;
        bool isValid = true;

        foreach (var entity in context.Model.Entities.Where(e => e.TenantScoped == true))
        {
            // Check if entity (or any parent in inheritance chain) has tenantId field
            bool hasTenantId = HasTenantIdInChain(entity, context.Model);

            // Check if TenantAware aspect is applied (on entity or any parent)
            bool hasTenantAwareAspect = HasTenantAwareAspectInChain(entity, context.Model);

            if (!hasTenantId && !hasTenantAwareAspect)
            {
                context.Diagnostics.Add(new CompilationDiagnostic(
                    DiagnosticSeverity.Error,
                    ErrorCodes.TENANT_MISSING_ID,
                    $"Entity '{entity.QualifiedName}' is marked as tenant-scoped but does not have 'tenantId' field. " +
                    $"Add the 'TenantAware' aspect or explicitly define a 'tenantId: UUID' field.",
                    entity.SourceFile,
                    entity.StartLine
                ));
                isValid = false;
            }
        }

        return isValid;
    }

    /// <summary>
    /// Validation 2: Detect problematic cross-tenant references.
    /// - Warn: Tenant-scoped entity → Global entity (valid, reference data)
    /// - Error: Global entity → Tenant-scoped entity (invalid, breaks isolation)
    /// </summary>
    private bool ValidateCrossTenantReferences(CompilationContext context)
    {
        if (context.Model == null) return true;
        bool isValid = true;

        foreach (var entity in context.Model.Entities)
        {
            // Check associations
            foreach (var assoc in entity.Associations)
            {
                var targetEntity = context.Model.FindEntity(assoc.TargetEntity);
                if (targetEntity == null) continue;

                // Invalid: Global entity references tenant-scoped entity
                if (!entity.TenantScoped && targetEntity.TenantScoped)
                {
                    context.Diagnostics.Add(new CompilationDiagnostic(
                        DiagnosticSeverity.Error,
                        ErrorCodes.TENANT_GLOBAL_REFS_SCOPED,
                        $"Global entity '{entity.QualifiedName}' cannot reference tenant-scoped entity '{targetEntity.QualifiedName}'. " +
                        $"This breaks tenant isolation. Either make '{entity.Name}' tenant-scoped or make '{targetEntity.Name}' global.",
                        entity.SourceFile,
                        entity.StartLine
                    ));
                    isValid = false;
                }

                // Valid but informational: Tenant-scoped entity references global entity
                if (entity.TenantScoped && !targetEntity.TenantScoped)
                {
                    context.Diagnostics.Add(new CompilationDiagnostic(
                        DiagnosticSeverity.Info,
                        ErrorCodes.TENANT_SCOPED_REFS_GLOBAL,
                        $"Tenant-scoped entity '{entity.QualifiedName}' references global entity '{targetEntity.QualifiedName}'. " +
                        $"This is typically used for reference data (e.g., Country, Currency).",
                        entity.SourceFile,
                        entity.StartLine
                    ));
                }
            }

            // Check compositions
            foreach (var comp in entity.Compositions)
            {
                var targetEntity = context.Model.FindEntity(comp.TargetEntity);
                if (targetEntity == null) continue;

                // Compositions must match parent's tenant-scoping
                if (entity.TenantScoped != targetEntity.TenantScoped)
                {
                    context.Diagnostics.Add(new CompilationDiagnostic(
                        DiagnosticSeverity.Warning,
                        ErrorCodes.TENANT_COMPOSITION_MISMATCH,
                        $"Entity '{entity.QualifiedName}' (tenant-scoped: {entity.TenantScoped}) composes '{targetEntity.QualifiedName}' " +
                        $"(tenant-scoped: {targetEntity.TenantScoped}). Compositions should match parent's tenant-scoping.",
                        entity.SourceFile,
                        entity.StartLine
                    ));
                }
            }
        }

        return isValid;
    }

    /// <summary>
    /// Validation 3: Access control scope must be compatible with entity tenant-scoping.
    /// </summary>
    private bool ValidateAccessControlScopes(CompilationContext context)
    {
        if (context.Model == null) return true;

        bool isValid = true;

        foreach (var acl in context.Model.AccessControls)
        {
            var targetEntity = context.Model.FindEntity(acl.TargetEntity);
            if (targetEntity == null) continue;

            foreach (var rule in acl.Rules)
            {
                // Skip if scope is not specified (will inherit default)
                if (rule.Scope == null) continue;

                // Error: GLOBAL scope on non-tenant-scoped entity is redundant
                if (rule.Scope == BmAccessScope.Global && !targetEntity.TenantScoped)
                {
                    context.Diagnostics.Add(new CompilationDiagnostic(
                        DiagnosticSeverity.Warning,
                        ErrorCodes.TENANT_REDUNDANT_GLOBAL_SCOPE,
                        $"Access control for global entity '{targetEntity.QualifiedName}' specifies GLOBAL scope, which is redundant. " +
                        $"Global entities are already accessible across tenants.",
                        acl.SourceFile,
                        acl.StartLine
                    ));
                }

                // Error: TENANT/COMPANY scope on global entity is invalid
                if ((rule.Scope == BmAccessScope.Tenant || rule.Scope == BmAccessScope.Company)
                    && !targetEntity.TenantScoped)
                {
                    context.Diagnostics.Add(new CompilationDiagnostic(
                        DiagnosticSeverity.Error,
                        ErrorCodes.TENANT_INVALID_SCOPE,
                        $"Access control for global entity '{targetEntity.QualifiedName}' cannot use {rule.Scope} scope. " +
                        $"Global entities are not tenant-scoped. Remove the scope or make the entity tenant-scoped.",
                        acl.SourceFile,
                        acl.StartLine
                    ));
                    isValid = false;
                }
            }
        }

        return isValid;
    }

    /// <summary>
    /// Validation 4: Module consistency warnings (non-fatal).
    /// </summary>
    private void ValidateModuleConsistency(CompilationContext context)
    {
        if (context.Model == null) return;
        var module = context.Model.Module;
        if (module?.TenantAware != true) return;

        // Check if all entities in tenant-aware module are tenant-scoped
        var nonTenantEntities = context.Model.Entities
            .Where(e => !e.TenantScoped && !e.IsExtension)
            .ToList();

        if (nonTenantEntities.Any())
        {
            context.Diagnostics.Add(new CompilationDiagnostic(
                DiagnosticSeverity.Warning,
                ErrorCodes.TENANT_MODULE_INCONSISTENCY,
                $"Module '{module.Name}' is marked as tenant-aware, but {nonTenantEntities.Count} " +
                $"entities are not tenant-scoped: {string.Join(", ", nonTenantEntities.Select(e => e.Name))}. " +
                $"Consider adding @GlobalScoped annotation if this is intentional.",
                module.SourceFile,
                module.StartLine ?? 0
            ));
        }
    }

    /// <summary>
    /// Validation 5: Warn if a service exposes tenant-scoped entities without being tenant-scoped itself.
    /// </summary>
    private void ValidateServiceTenantScoping(CompilationContext context)
    {
        if (context.Model == null) return;

        foreach (var service in context.Model.Services)
        {
            bool serviceIsTenantScoped = service.HasAnnotation("TenantScoped");

            // Check if service exposes any tenant-scoped entities
            foreach (var exposedEntity in service.Entities)
            {
                // Service entity exposures store the source entity name in Aspects[0]
                // (from 'entity Customers as Customer' syntax)
                var sourceEntityName = exposedEntity.Aspects.FirstOrDefault() ?? exposedEntity.Name;
                var entityDef = context.Model.FindEntity(sourceEntityName)
                    ?? context.Model.FindEntity(exposedEntity.Name)
                    ?? context.Model.FindEntity(exposedEntity.QualifiedName);
                if (entityDef == null) continue;

                if (entityDef.TenantScoped && !serviceIsTenantScoped)
                {
                    context.Diagnostics.Add(new CompilationDiagnostic(
                        DiagnosticSeverity.Warning,
                        ErrorCodes.TENANT_SERVICE_EXPOSES_SCOPED,
                        $"Service '{service.Name}' exposes tenant-scoped entity '{entityDef.QualifiedName}' but is not marked as @TenantScoped. " +
                        $"Consider adding @TenantScoped annotation to the service.",
                        service.SourceFile,
                        service.StartLine
                    ));
                }
            }

            // Check if service event handlers reference tenant-scoped entities
            foreach (var handler in service.EventHandlers)
            {
                // Look up the event to check if it relates to tenant-scoped entities
                var eventDef = context.Model.Events.FirstOrDefault(e =>
                    e.Name == handler.EventName || e.QualifiedName == handler.EventName);
                if (eventDef == null) continue;

                // If the event has a @TenantScoped annotation and the service doesn't
                if (eventDef.HasAnnotation("TenantScoped") && !serviceIsTenantScoped)
                {
                    context.Diagnostics.Add(new CompilationDiagnostic(
                        DiagnosticSeverity.Warning,
                        ErrorCodes.TENANT_HANDLER_REFS_SCOPED,
                        $"Service '{service.Name}' handles tenant-scoped event '{handler.EventName}' but is not marked as @TenantScoped. " +
                        $"Ensure tenant context is properly propagated in the event handler.",
                        handler.SourceFile ?? service.SourceFile,
                        handler.StartLine > 0 ? handler.StartLine : service.StartLine
                    ));
                }
            }
        }
    }

    /// <summary>
    /// Walk the inheritance chain to check if any entity has a tenantId field.
    /// </summary>
    private static bool HasTenantIdInChain(BmEntity entity, BmModel model)
    {
        var current = entity;
        var visited = new HashSet<string>();
        while (current != null)
        {
            if (!visited.Add(current.QualifiedName)) break; // prevent cycles

            if (current.Fields.Any(f =>
                f.Name.Equals("tenantId", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (string.IsNullOrEmpty(current.ParentEntityName)) break;
            current = model.FindEntity(current.ParentEntityName);
        }
        return false;
    }

    /// <summary>
    /// Walk the inheritance chain to check if any entity has the TenantAware aspect.
    /// </summary>
    private static bool HasTenantAwareAspectInChain(BmEntity entity, BmModel model)
    {
        var current = entity;
        var visited = new HashSet<string>();
        while (current != null)
        {
            if (!visited.Add(current.QualifiedName)) break;

            if (current.Aspects.Any(a =>
                a == "TenantAware" || a.EndsWith(".TenantAware")))
                return true;

            if (string.IsNullOrEmpty(current.ParentEntityName)) break;
            current = model.FindEntity(current.ParentEntityName);
        }
        return false;
    }
}
