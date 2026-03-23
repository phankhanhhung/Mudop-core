# Database Test Inventory - Complete Analysis

**Date**: 2026-01-23  
**Projects**: BMMDL.Tests (Unit) và BMMDL.Tests.E2E

---

## 📊 BMMDL.Tests (Unit Tests)

### ✅ Tests DÙNG Database (4 classes)

| # | Test Class | Base Class | Database Type | Purpose |
|---|-----------|-----------|---------------|---------|
| 1 | **EfCoreOptimizationBenchmarkTests** | `DatabaseIntegrationTestBase` | PostgreSQL | Performance benchmarks |
| 2 | **ChildIdentityPreservationTests** | `DatabaseIntegrationTestBase` | PostgreSQL | Identity preservation |
| 3 | **PostgresSchemaReaderTests** | `DatabaseIntegrationTestBase` | PostgreSQL | Schema reading |
| 4 | **MigrationExecutorTests** | `DatabaseIntegrationTestBase` | PostgreSQL | Migration execution |

### ❌ Tests KHÔNG DÙNG Database (~70 classes)

**Categories:**
- **CodeGen Tests** (~20 classes) - Pure logic, no DB
  - AssociationNavigationTests
  - ComputedFieldDdlGenerationTests
  - ConstraintGenerationTests
  - ExpressionTranslatorTests
  - FunctionMappingTests
  - NamingConventionTests
  - SequenceGeneratorTests
  - TemporalDdlGenerationTests
  - TriggerGenerationTests
  - TypeCastingAdvancedTests
  - etc.

- **Compiler Tests** (~10 classes) - Pure logic, no DB
  - BmmdlValidatorTests
  - ComputedFieldValidatorTests
  - ModelDiffEngineTests
  - ModuleDependencyResolverTests
  - SemanticValidationTests
  - TemporalValidationTests
  - etc.

- **Parser Tests** (~7 classes) - Pure logic, no DB
  - BmExpressionBuilderTests
  - BmModelTests
  - BmmdlModelBuilderTests
  - CardinalityParsingTests
  - ParserIntegrationTests
  - TemporalParserTests
  - etc.

- **Registry Tests** (~5 classes) - InMemory or mocked
  - ChangeDetectorTests
  - DefinitionHasherTests
  - UpgradeJobServiceTests (InMemory DB)
  - etc.

- **MetaModel Tests** (~3 classes) - Pure logic
  - EntityTests
  - ExpressionTests
  - TypeReferenceTests

---

## 📊 BMMDL.Tests.E2E (E2E Tests)

### ✅ Tests DÙNG Database (20 classes)

#### **Group 1: DatabaseIntegrationTestBase** (1 class)

| # | Test Class | Base Class | Purpose |
|---|-----------|-----------|---------|
| 1 | **PostgresTemporalCapabilityTests** | `DatabaseIntegrationTestBase` | PostgreSQL temporal features |

#### **Group 2: IsolatedE2ETestBase** (2 classes)

| # | Test Class | Base Class | Features |
|---|-----------|-----------|----------|
| 1 | **TenantCrudTests** | `IsolatedE2ETestBase` | ✅ User + Login + Tenant |
| 2 | **IsolatedTenantTests** | `IsolatedE2ETestBase` | ✅ User + Login + Tenant |

#### **Group 3: IAsyncLifetime (Manual Setup)** (17 classes)

| # | Test Class | Base Class | Database Setup |
|---|-----------|-----------|----------------|
| 1 | **SchemaInitializationServiceTests** | `IAsyncLifetime` | Manual connection |
| 2 | **RegistryDrivenSchemaInitTests** | `IAsyncLifetime` | Manual connection |
| 3 | **FullIntegrationTests** | `IAsyncLifetime` | Manual connection |
| 4 | **RegistryDrivenSchemaInitIntegrationTest** | `IAsyncLifetime` | Manual connection |
| 5 | **CompilerPipelineIntegrationTest** | `IAsyncLifetime` | Manual connection |
| 6 | **WarehouseTemporalE2ETests** | `IAsyncLifetime` | Manual + E2EFixture |
| 7 | **VersioningWorkflowE2ETests** | `IAsyncLifetime` | Manual + E2EFixture |
| 8 | **VersioningE2ETests** | `IAsyncLifetime` | Manual + E2EFixture |
| 9 | **TenantModuleVersioningE2ETests** | `IAsyncLifetime` | Manual + E2EFixture |
| 10 | **TemporalEdgeCaseTests** | `IAsyncLifetime` | Manual + E2EFixture |
| 11 | **AuthenticationE2ETests** | `IAsyncLifetime` | Manual + E2EFixture |
| 12 | **AdminBootstrapE2ETests** | `IAsyncLifetime` | Manual + E2EFixture |
| 13 | **AllModulesCompilationTests** | `IAsyncLifetime` | Manual connection |
| 14 | **CompilerPipelineIntegrationTests** | `IAsyncLifetime` | Manual connection |
| 15 | **DbPersistenceServiceTests** | `IAsyncLifetime` | Manual connection |
| 16 | **DebugModulePublishTests** | `IAsyncLifetime` | Manual connection |
| 17 | **[Others]** | `IAsyncLifetime` | Manual connection |

---

## 🎯 Summary Statistics

### BMMDL.Tests (Unit)
```
Total Test Classes: ~74
├─ Using Database: 4 (5.4%)
│  └─ DatabaseIntegrationTestBase: 4
└─ Not Using Database: ~70 (94.6%)
   ├─ Pure Logic Tests: ~60
   └─ InMemory Tests: ~10
```

### BMMDL.Tests.E2E
```
Total Test Classes: ~24
├─ Using Database: 20 (83.3%)
│  ├─ DatabaseIntegrationTestBase: 1
│  ├─ IsolatedE2ETestBase: 2
│  └─ IAsyncLifetime (manual): 17
└─ Not Using Database: ~4 (16.7%)
```

---

## 📋 Detailed Breakdown

### BMMDL.Tests - Database Tests

#### 1. **EfCoreOptimizationBenchmarkTests**
```csharp
[Trait("Category", "Performance")]
[Trait("Category", "Integration")]
public class EfCoreOptimizationBenchmarkTests : DatabaseIntegrationTestBase
```
- **Database**: PostgreSQL (external)
- **Setup**: Creates RegistryDbContext, seeds test data
- **Tests**: 5 performance benchmarks
- **Purpose**: Measure EF Core optimization impact

#### 2. **ChildIdentityPreservationTests**
```csharp
[Trait("Category", "Integration")]
public class ChildIdentityPreservationTests : DatabaseIntegrationTestBase
```
- **Database**: PostgreSQL (external)
- **Setup**: Creates RegistryDbContext, creates tenant
- **Tests**: 3 identity preservation tests
- **Purpose**: Verify Field/Index IDs preserved during upgrades

#### 3. **PostgresSchemaReaderTests**
```csharp
[Trait("Category", "Integration")]
public class PostgresSchemaReaderTests : DatabaseIntegrationTestBase
```
- **Database**: PostgreSQL (external)
- **Setup**: Creates test schema with tables/indexes/constraints
- **Tests**: 11 schema reading tests
- **Purpose**: Verify PostgresSchemaReader can read all schema elements

#### 4. **MigrationExecutorTests**
```csharp
[Trait("Category", "Integration")]
public class MigrationExecutorTests : DatabaseIntegrationTestBase
```
- **Database**: PostgreSQL (external)
- **Setup**: Cleanup migration tables
- **Tests**: 10 migration execution tests
- **Purpose**: Verify migration apply/rollback/history

---

### BMMDL.Tests.E2E - Database Tests

#### Group 1: DatabaseIntegrationTestBase (1 class)

##### **PostgresTemporalCapabilityTests**
```csharp
[Trait("Category", "Integration")]
public class PostgresTemporalCapabilityTests : DatabaseIntegrationTestBase
```
- **Database**: PostgreSQL (external)
- **Setup**: Creates test schema with temporal tables
- **Tests**: 3 temporal capability tests
- **Purpose**: Verify PostgreSQL temporal features

#### Group 2: IsolatedE2ETestBase (2 classes)

##### **TenantCrudTests**
```csharp
[Collection("E2E")]
public class TenantCrudTests : IsolatedE2ETestBase
```
- **Database**: PostgreSQL + APIs
- **Setup**: ✅ Full bootstrap (user + login + tenant)
- **Tests**: CRUD operations on tenants
- **Purpose**: E2E tenant management

##### **IsolatedTenantTests**
```csharp
[Collection("E2E")]
public class IsolatedTenantTests : IsolatedE2ETestBase
```
- **Database**: PostgreSQL + APIs
- **Setup**: ✅ Full bootstrap (user + login + tenant)
- **Tests**: Tenant isolation tests
- **Purpose**: Verify tenant isolation

#### Group 3: IAsyncLifetime - Manual Setup (17 classes)

**Pattern:**
```csharp
public class XxxTests : IAsyncLifetime
{
    private NpgsqlConnection? _connection;
    
    public async Task InitializeAsync()
    {
        _connection = new NpgsqlConnection(...);
        await _connection.OpenAsync();
        // Manual setup
    }
}
```

**Issues:**
- ❌ Duplicate connection management code
- ❌ No standardized base class
- ⚠️ Some use E2EFixture, some don't
- ⚠️ Inconsistent patterns

---

## 🎯 Base Class Usage Summary

### BMMDL.Tests (Unit)

| Base Class | Count | Tests |
|-----------|-------|-------|
| **DatabaseIntegrationTestBase** | 4 | EfCoreOptimizationBenchmarkTests, ChildIdentityPreservationTests, PostgresSchemaReaderTests, MigrationExecutorTests |
| **None** (pure logic) | ~70 | All other tests |

### BMMDL.Tests.E2E

| Base Class | Count | Tests |
|-----------|-------|-------|
| **DatabaseIntegrationTestBase** | 1 | PostgresTemporalCapabilityTests |
| **IsolatedE2ETestBase** | 2 | TenantCrudTests, IsolatedTenantTests |
| **IAsyncLifetime** (manual) | 17 | SchemaInitializationServiceTests, FullIntegrationTests, WarehouseTemporalE2ETests, etc. |

---

## 🚨 Issues Found

### BMMDL.Tests.E2E

#### ❌ **17 tests dùng IAsyncLifetime manual setup**

**Problem:**
- Duplicate connection management code
- No fail-fast pattern
- Inconsistent error handling
- Hard to maintain

**Should be:**
- Using `DatabaseIntegrationTestBase` for simple DB tests
- Using `IsolatedE2ETestBase` for full E2E tests

#### ⚠️ **Mixed patterns**

Some tests use:
- E2EFixture (WebApplicationFactory)
- Manual NpgsqlConnection
- Both combined

**Needs standardization!**

---

## ✅ Recommendations

### For BMMDL.Tests (Unit)
```
✅ Current state is GOOD
✅ 4 tests using DatabaseIntegrationTestBase
✅ Consistent pattern
✅ Fail-fast implemented
```

### For BMMDL.Tests.E2E
```
⚠️ Needs refactoring

17 tests should migrate to base classes:
├─ Simple DB tests → DatabaseIntegrationTestBase
├─ Full E2E tests → IsolatedE2ETestBase
└─ Shared state tests → SharedStateE2ETestBase
```

---

## 📊 Migration Candidates (E2E)

### Should use DatabaseIntegrationTestBase (8 tests)
- SchemaInitializationServiceTests
- RegistryDrivenSchemaInitTests
- RegistryDrivenSchemaInitIntegrationTest
- CompilerPipelineIntegrationTest
- AllModulesCompilationTests
- CompilerPipelineIntegrationTests
- DbPersistenceServiceTests
- DebugModulePublishTests

### Should use IsolatedE2ETestBase (9 tests)
- WarehouseTemporalE2ETests
- VersioningWorkflowE2ETests
- VersioningE2ETests
- TenantModuleVersioningE2ETests
- TemporalEdgeCaseTests
- AuthenticationE2ETests
- AdminBootstrapE2ETests
- FullIntegrationTests (if needs API)
- [Others that need user/tenant]

---

## 🎉 Conclusion

### BMMDL.Tests (Unit)
- ✅ **4/74 tests** use database
- ✅ All using `DatabaseIntegrationTestBase`
- ✅ **Consistent and clean**

### BMMDL.Tests.E2E
- ⚠️ **20/24 tests** use database
- ❌ Only **3/20** using proper base classes
- ❌ **17/20** using manual IAsyncLifetime
- ⚠️ **Needs standardization**

**Next step:** Migrate 17 E2E tests to proper base classes!
