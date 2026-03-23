# Configuration Management Module - ARCHIVED

**Status:** ARCHIVED (January 10, 2026)
**Reason:** Architecture design flaw - layering violation
**Replaced by:** `BMMDL.Infrastructure` project with hardcoded EF Core entities

---

## Why This Module Was Archived

This module attempted to define system configuration (User, Tenant, DatabaseEndpoint, etc.) as BMMDL entities. This creates a **chicken-and-egg problem** that is architecturally unsolvable:

### The Bootstrap Paradox

```
1. To compile BMMDL module → Need BMMDL Compiler
2. To run Compiler → Need Database Connection
3. To get Connection → Need Configuration (from 10_config_management module)
4. To load Config → Need to compile module 10_config_management
5. Go to step 1 → DEADLOCK!
```

### Additional Problems

1. **Layering Violation**
   - Configuration is Infrastructure Layer (Layer 0)
   - BMMDL entities are Business Domain Layer (Layer 2)
   - Layer 2 cannot define Layer 0 - this is reverse dependency

2. **Missing Foundation Entities**
   - Module inherits `Auditable` aspect (needs `createdBy`, `modifiedBy`)
   - `Auditable` requires User entity to exist
   - But User entity is not defined anywhere in BMMDL!
   - Same problem with Tenant entity

3. **Industry Anti-Pattern**
   - No major platform defines infrastructure in business DSL:
     - ASP.NET Identity: Hardcoded `IdentityUser` (not in business model)
     - Prisma: User/Tenant external to Prisma schema
     - Hasura: `auth.users` in separate schema (not GraphQL metadata)
     - Supabase: `auth` schema hardcoded (not in migrations)
     - SAP CAP: User from SAP IAS (not in CDS)

---

## The Correct Solution

Configuration and infrastructure tables are now defined in:

**`src/BMMDL.Infrastructure/`** - Hardcoded EF Core entities

### Infrastructure Layer (Layer 0)

```
Infrastructure Tables (Hardcoded C#)
├── users
├── tenants
├── roles
├── permissions
├── system_configurations
├── database_endpoints
├── storage_providers
├── auth_providers
└── audit_logs
```

**Key Properties:**
- ✅ Exist BEFORE any BMMDL compilation
- ✅ Created by EF Core migrations (not BMMDL compiler)
- ✅ Can bootstrap configuration from environment variables
- ✅ Follows industry standard patterns

### Bootstrap Sequence (Correct)

```
1. Application starts
2. Load bootstrap config from environment variables
   └─> BOOTSTRAP_POSTGRES_HOST, BOOTSTRAP_POSTGRES_DB, etc.
3. Connect to Infrastructure database using bootstrap config
4. Query SystemConfiguration table for runtime config
5. Use runtime config to compile BMMDL modules
6. Generate business entity tables
```

---

## What This Module Defined (Reference Only)

This file is kept for **reference and documentation purposes only**. The entities defined here were:

### Entities

1. **SystemConfiguration** - Main config container
2. **DatabaseEndpoint** - Database connection configs
3. **StorageProvider** - S3/MinIO/Azure Blob configs
4. **AuthProvider** - OAuth2/OIDC/SAML configs
5. **CoreTableMapping** - Schema mappings for core tables
6. **ConfigurationHistory** - Audit trail

### Services

- `ConfigurationService` with operations for querying configuration

### Rules

- Validation rules for each configuration entity
- Access control policies for RBAC

---

## Migration Path

If you were using this module:

### Before (WRONG)
```bmmdl
module ConfigManagement version '1.0.0' {
    depends on Foundation version '>=1.0.0';
}

entity SystemConfiguration : Auditable {
    configName: String(100);
    databaseEndpoints: Composition of DatabaseEndpoint;
}
```

### After (CORRECT)
```csharp
// src/BMMDL.Infrastructure/Data/Entities/SystemConfiguration.cs
public class SystemConfiguration
{
    public Guid Id { get; set; }
    public string ConfigName { get; set; } = string.Empty;
    public ICollection<DatabaseEndpoint> DatabaseEndpoints { get; set; }
    // ...
}

// src/BMMDL.Infrastructure/Data/InfrastructureDbContext.cs
public class InfrastructureDbContext : DbContext
{
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    // ...
}
```

### Bootstrap Configuration

**Environment Variables (Tier 1):**
```bash
# Minimal config to connect to Infrastructure database
BOOTSTRAP_POSTGRES_HOST=localhost
BOOTSTRAP_POSTGRES_PORT=5432
BOOTSTRAP_POSTGRES_DB=bmmdl_infrastructure
BOOTSTRAP_POSTGRES_USER=postgres
BOOTSTRAP_POSTGRES_PASSWORD=postgres
```

**Runtime Configuration (Tier 2):**
```csharp
// Load from Infrastructure database after bootstrap
var config = await infrastructureDb.SystemConfigurations
    .Include(c => c.DatabaseEndpoints)
    .Include(c => c.StorageProviders)
    .FirstAsync(c => c.Environment == "Production" && c.IsActive);
```

---

## Lessons Learned

### 1. Not Everything Should Be a DSL Entity

Some things are **too foundational** to be defined in the DSL:
- User, Tenant, Role, Permission (identity)
- Configuration, Settings (bootstrap)
- Audit logs, System tables (infrastructure)

These belong in the **platform layer**, not the **business layer**.

### 2. Layering Is Critical

```
Layer 0: Infrastructure (hardcoded)
    └─> Layer 1: Meta-Model (BMMDL schema storage)
        └─> Layer 2: Business Domain (BMMDL entities)
```

**NEVER** let Layer 2 define Layer 0. This is a fundamental architecture principle.

### 3. Bootstrap Configuration Must Be External

You cannot store bootstrap configuration in the database you're trying to connect to. It must come from:
- Environment variables
- Configuration files (appsettings.json)
- Kubernetes Secrets/ConfigMaps
- External config services (Consul, Vault)

---

## References

- **New Implementation:** `src/BMMDL.Infrastructure/`
- **Architecture Doc:** `docs/INFRASTRUCTURE_DESIGN.md`
- **Updated CLAUDE.md:** Section on "Infrastructure Layer"

---

**Archive Date:** January 10, 2026
**Archived By:** Claude (AI Assistant)
**Decision:** Approved by architecture review
