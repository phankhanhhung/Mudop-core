# Configuration Management Module

**Version:** 1.0.0
**Namespace:** Config
**Dependencies:** Foundation (01_core)

## Overview

The Configuration Management module provides centralized system-wide configuration for:

- **Database Endpoints** - Connection details for business entity databases
- **Storage Providers** - Configuration for file storage (S3, MinIO, Azure, GCS, Local)
- **Authentication Providers** - OAuth2, OIDC, SAML, LDAP, JWT configurations
- **Core Table Mappings** - Schema mappings for user, tenant, role, permission tables

## Problem Solved

This module addresses the following questions:
- ✅ **Which DB endpoint should business entities use?** → `DatabaseEndpoint` entity
- ✅ **Which storage provider is configured?** → `StorageProvider` entity
- ✅ **Where is the auth provider?** → `AuthProvider` entity
- ✅ **Where are user/tenant/role tables?** → `CoreTableMapping` entity

## Key Features

### 1. Multi-Environment Support
Separate configurations for Development, Staging, Production, and Test environments.

### 2. Security
- Sensitive fields marked with `@Sensitive` annotation
- Field-level access control (e.g., developers cannot read passwords)
- Encrypted credentials storage (in real implementation)

### 3. Health Monitoring
Each endpoint/provider tracks:
- `lastHealthCheck` - Last health check timestamp
- `healthStatus` - Current status (Healthy/Degraded/Unavailable)

### 4. Audit Trail
`ConfigurationHistory` entity tracks all changes with:
- Before/after snapshots
- Change description
- Who made the change and when

### 5. Priority and Fallback
- Primary/replica configuration for databases
- Priority-based fallback for redundancy

## Entity Descriptions

### SystemConfiguration
Main configuration container linking all settings together.

**Key Fields:**
- `configName` - Configuration name (e.g., "Production-US-East")
- `environment` - Environment type
- `isDefault` - Is this the default configuration?
- `effectiveFrom/effectiveUntil` - Validity period

### DatabaseEndpoint
Database connection configuration.

**Key Fields:**
- `endpointName` - Endpoint identifier
- `provider` - PostgreSQL, MySQL, SQLServer, Oracle, MongoDB
- `purpose` - What this database is for (e.g., "BusinessEntities", "Analytics")
- `host`, `port`, `databaseName` - Connection details
- `minPoolSize`, `maxPoolSize` - Connection pool configuration
- `isPrimary` - Primary vs replica flag

**Example Use Case:**
```bmmdl
// Business entities go to primary PostgreSQL
DatabaseEndpoint {
    endpointName: "BizDB-Primary",
    provider: PostgreSQL,
    purpose: "BusinessEntities",
    host: "db.company.com",
    port: 5432,
    isPrimary: true
}

// Analytics queries go to read replica
DatabaseEndpoint {
    endpointName: "BizDB-Analytics",
    provider: PostgreSQL,
    purpose: "Analytics",
    host: "db-replica.company.com",
    port: 5432,
    isPrimary: false
}
```

### StorageProvider
File storage configuration for FileReference fields.

**Key Fields:**
- `providerName` - Provider identifier
- `providerType` - S3, MinIO, AzureBlob, GCS, Local
- `purpose` - Storage purpose (e.g., "DocumentStorage", "ImageStorage")
- `endpoint`, `region`, `bucketName` - Provider-specific settings
- `maxFileSize` - Maximum file size limit
- `allowedMimeTypes` - Allowed file types (JSON array)

**Example Use Case:**
```bmmdl
// Documents stored in S3
StorageProvider {
    providerName: "S3-Documents",
    providerType: S3,
    purpose: "DocumentStorage",
    endpoint: "s3.amazonaws.com",
    region: "us-east-1",
    bucketName: "company-documents",
    maxFileSize: 52428800  // 50MB
}

// Images stored in MinIO (on-premise)
StorageProvider {
    providerName: "MinIO-Images",
    providerType: MinIO,
    purpose: "ImageStorage",
    endpoint: "minio.company.internal",
    bucketName: "images",
    maxFileSize: 10485760  // 10MB
}
```

### AuthProvider
Authentication provider configuration.

**Key Fields:**
- `providerName` - Provider identifier
- `providerType` - OAuth2, OIDC, SAML, LDAP, JWT, Custom
- `issuer`, `authorizationEndpoint`, `tokenEndpoint` - OAuth2/OIDC endpoints
- `clientId`, `clientSecret` - OAuth client credentials
- `ldapServer`, `baseDn` - LDAP configuration
- `isPrimary` - Primary authentication provider

**Example Use Case:**
```bmmdl
// Corporate SSO (OIDC)
AuthProvider {
    providerName: "Corporate-SSO",
    providerType: OIDC,
    issuer: "https://sso.company.com",
    authorizationEndpoint: "https://sso.company.com/oauth/authorize",
    tokenEndpoint: "https://sso.company.com/oauth/token",
    isPrimary: true
}

// Internal LDAP (fallback)
AuthProvider {
    providerName: "Internal-LDAP",
    providerType: LDAP,
    ldapServer: "ldap.company.internal",
    ldapPort: 389,
    baseDn: "dc=company,dc=com",
    isPrimary: false
}
```

### CoreTableMapping
Schema mappings for core system tables.

**Key Fields:**
- `tableType` - Type of table (User, Tenant, Role, Permission, etc.)
- `databaseEndpoint` - Which database contains this table
- `schemaName`, `tableName` - Table location
- `primaryKeyColumn` - Primary key column name
- `columnMappings` - JSON mapping of logical to physical columns

**Example Use Case:**
```bmmdl
// User table mapping
CoreTableMapping {
    tableType: "User",
    databaseEndpoint: -> AuthDB,
    schemaName: "auth",
    tableName: "users",
    primaryKeyColumn: "user_id",
    columnMappings: '{"id": "user_id", "email": "email_address", "name": "full_name"}'
}

// Tenant table mapping
CoreTableMapping {
    tableType: "Tenant",
    databaseEndpoint: -> AuthDB,
    schemaName: "auth",
    tableName: "tenants",
    primaryKeyColumn: "tenant_id",
    columnMappings: '{"id": "tenant_id", "name": "tenant_name", "subdomain": "tenant_subdomain"}'
}

// Role table mapping (in separate security DB)
CoreTableMapping {
    tableType: "Role",
    databaseEndpoint: -> SecurityDB,
    schemaName: "rbac",
    tableName: "system_roles",
    primaryKeyColumn: "role_id",
    columnMappings: '{"id": "role_id", "name": "role_name", "permissions": "role_permissions"}'
}
```

## Usage Examples

### 1. Configure Production Environment

```bmmdl
// Create main configuration
SystemConfiguration {
    ID: <guid>,
    configName: "Production-Global",
    environment: Production,
    status: Active,
    version: "1.0.0",
    effectiveFrom: "2026-01-10T00:00:00Z",
    isDefault: true,
    isActive: true
}

// Add primary database
DatabaseEndpoint {
    ID: <guid>,
    endpointName: "Prod-Primary-DB",
    provider: PostgreSQL,
    purpose: "BusinessEntities",
    host: "prod-db-primary.company.com",
    port: 5432,
    databaseName: "bmmdl_prod",
    username: "bmmdl_app",
    password: "<encrypted>",
    useSsl: true,
    sslMode: "verify-full",
    minPoolSize: 10,
    maxPoolSize: 100,
    connectionTimeout: 30,
    isPrimary: true,
    environment: Production,
    systemConfig: -> "Production-Global"
}

// Add S3 storage
StorageProvider {
    ID: <guid>,
    providerName: "Prod-S3-Storage",
    providerType: S3,
    purpose: "DocumentStorage",
    endpoint: "s3.amazonaws.com",
    region: "us-east-1",
    bucketName: "company-prod-documents",
    accessKey: "<encrypted>",
    secretKey: "<encrypted>",
    useSsl: true,
    maxFileSize: 104857600,  // 100MB
    allowedMimeTypes: '["application/pdf", "image/jpeg", "image/png"]',
    isDefault: true,
    environment: Production,
    systemConfig: -> "Production-Global"
}

// Add OIDC auth
AuthProvider {
    ID: <guid>,
    providerName: "Prod-SSO",
    providerType: OIDC,
    issuer: "https://sso.company.com",
    authorizationEndpoint: "https://sso.company.com/oauth/authorize",
    tokenEndpoint: "https://sso.company.com/oauth/token",
    userInfoEndpoint: "https://sso.company.com/oauth/userinfo",
    jwksUri: "https://sso.company.com/.well-known/jwks.json",
    clientId: "bmmdl-prod-client",
    clientSecret: "<encrypted>",
    scopes: "openid profile email",
    isPrimary: true,
    environment: Production,
    systemConfig: -> "Production-Global"
}

// Map core tables
CoreTableMapping {
    ID: <guid>,
    tableType: "User",
    databaseEndpoint: -> "Prod-Primary-DB",
    schemaName: "auth",
    tableName: "users",
    primaryKeyColumn: "id",
    columnMappings: '{"id": "id", "email": "email", "name": "full_name", "tenantId": "tenant_id"}',
    isActive: true,
    environment: Production,
    systemConfig: -> "Production-Global"
}
```

### 2. Query Configuration (via Service)

```bmmdl
// Get active configuration for production
var config = ConfigurationService.GetActiveConfiguration(Production);

// Get database endpoint for business entities
var dbEndpoint = ConfigurationService.GetDatabaseEndpoint("BusinessEntities");
// Returns: host, port, credentials for business entity database

// Get storage provider for documents
var storage = ConfigurationService.GetStorageProvider("DocumentStorage");
// Returns: S3 configuration with bucket, credentials

// Get primary auth provider
var auth = ConfigurationService.GetAuthProvider();
// Returns: OIDC/OAuth2 configuration

// Get user table mapping
var userTable = ConfigurationService.GetCoreTableMapping("User");
// Returns: schema, table name, column mappings
```

## Validation Rules

The module includes comprehensive validation:

### SystemConfiguration
- Configuration name is required
- Version is required
- Effective from date is required
- Default configuration must be active

### DatabaseEndpoint
- Endpoint name, host required
- Port must be between 1-65535
- Min pool size ≤ Max pool size
- Connection timeout must be positive

### StorageProvider
- Provider name, endpoint, bucket required
- Max file size must be positive

### AuthProvider
- Provider name, client ID, client secret required

### CoreTableMapping
- Table type, schema, table name required
- Primary key column required
- Column mappings required

## Access Control

The module implements strict role-based access control:

| Role | SystemConfiguration | DatabaseEndpoint | StorageProvider | AuthProvider | CoreTableMapping |
|------|---------------------|------------------|-----------------|--------------|------------------|
| **SuperAdmin** | ALL | ALL | ALL | ALL | ALL |
| **SystemAdmin** | READ | READ | READ | READ | READ |
| **DevOps** | READ | READ | READ | READ | READ |
| **Developer** | DENY UPDATE/DELETE | DENY credentials | DENY credentials | DENY credentials | READ |
| **Auditor** | - | - | - | - | READ (history) |

**Sensitive Field Protection:**
- Developers cannot read `password`, `connectionString`, `accessKey`, `secretKey`, `clientSecret`, `bindPassword`

## Integration with BMMDL Features

### Works with FileReference Type
When entities use `FileReference` type:
```bmmdl
entity Document {
    @Storage.Provider: 'S3'
    @Storage.Bucket: 'company-documents'
    content: FileReference;
}
```

The system looks up the `StorageProvider` configuration with `providerType = S3` and `bucketName = 'company-documents'` to determine the actual S3 endpoint, credentials, and region.

### Works with Multi-Tenancy
- `@GlobalScoped` - Configuration is system-wide, not tenant-specific
- Core table mappings support tenant-scoped tables

### Works with Access Control
- Field-level security on sensitive data
- Role-based access control for admin operations

## Deployment

### Compilation
```bash
dotnet run --project src/BMMDL.Compiler -- pipeline erp_modules/10_config_management/module.bmmdl -r
```

### Publish to Database
```bash
dotnet run --project src/BMMDL.Compiler -- pipeline erp_modules/10_config_management/module.bmmdl -r -p \
  -c "Host=localhost;Port=5432;Database=bmmdl;Username=postgres;Password=postgres" \
  --tenant 00000000-0000-0000-0000-000000000001
```

### Generated Tables
- `system_configurations`
- `database_endpoints`
- `storage_providers`
- `auth_providers`
- `core_table_mappings`
- `configuration_histories`

## Future Enhancements

1. **Secret Management Integration**
   - HashiCorp Vault integration
   - AWS Secrets Manager support
   - Azure Key Vault support

2. **Dynamic Reloading**
   - Hot-reload configuration without restart
   - Configuration change notifications

3. **Environment Promotion**
   - Copy configuration from Dev → Staging → Production
   - Configuration diff and validation

4. **Configuration Templates**
   - Pre-defined templates for common scenarios
   - Import/export configuration as JSON/YAML

5. **Health Dashboard**
   - Real-time health monitoring UI
   - Automatic failover on endpoint failure

## Version History

- **1.0.0** (2026-01-10) - Initial release
  - Database endpoint configuration
  - Storage provider configuration
  - Auth provider configuration
  - Core table mapping
  - Validation rules and access control
