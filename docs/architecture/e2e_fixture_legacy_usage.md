# Tests Still Using E2EFixture (Legacy)

**Date**: 2026-01-24  
**Status**: ⚠️ Legacy - Should be migrated

---

## 📊 Tests Using E2EFixture

### **Direct Test Classes** (13 tests):

1. ✅ `SchemaInitializationServiceTests.cs`
2. ✅ `WarehouseTemporalE2ETests.cs`
3. ✅ `VersioningWorkflowE2ETests.cs`
4. ✅ `VersioningE2ETests.cs`
5. ✅ `TenantModuleVersioningE2ETests.cs`
6. ✅ `TenantCrudTests.cs`
7. ✅ `TemporalEdgeCaseTests.cs`
8. ✅ `IsolatedTenantTests.cs`

### **Base Classes** (used by many tests):

9. ✅ `E2EWithDatabaseTestBase.cs` - Base class for DB tests
10. ✅ `E2ETestBootstrapper.cs` - Bootstrap helper
11. ✅ `E2ETestBase.cs` - Contains `IsolatedE2ETestBase` and `SharedStateE2ETestBase`

### **Collection Definition**:

12. ✅ `E2ECollection` - xUnit collection fixture

---

## 🤔 Why Are They Still Using E2EFixture?

### **Reason 1: Haven't Been Migrated Yet**

Most tests were written BEFORE the lazy loading refactoring. They still work with `E2EFixture` because:

```csharp
// E2EFixture still works! It just has extra overhead
await EnsureRegistrySchemaExistsAsync();  // Extra step, but harmless
await CreateClient();
```

**Impact**: 
- ✅ Tests still pass
- ⚠️ Slightly slower (direct SQL overhead)
- ⚠️ Violates "API-only" principle

---

### **Reason 2: Base Class Dependencies**

Many tests inherit from base classes that use `E2EFixture`:

```csharp
// E2EWithDatabaseTestBase.cs
protected E2EWithDatabaseTestBase(E2EFixture fixture, ITestOutputHelper output)
{
    _fixture = fixture;
}

// Tests inheriting from this base class
public class WarehouseTemporalE2ETests : E2EWithDatabaseTestBase
{
    public WarehouseTemporalE2ETests(E2EFixture fixture, ITestOutputHelper output)
        : base(fixture, output, includeWarehouseTest: true)
    {
    }
}
```

**To migrate**: Must update base class first, then all derived classes

---

### **Reason 3: Collection Fixture**

```csharp
// E2EFixture.cs line 308
[CollectionDefinition("E2E")]
public class E2ECollection : ICollectionFixture<E2EFixture>
{
}

// Tests using this collection
[Collection("E2E")]
public class MyTest
{
    public MyTest(E2EFixture fixture) { }
}
```

**To migrate**: Change collection to use `E2EStep1Fixture` or `E2EStep2Fixture`

---

## 📋 Detailed Analysis

### **1. SchemaInitializationServiceTests.cs**

```csharp
[Collection("E2E")]
public class SchemaInitializationServiceTests
{
    public SchemaInitializationServiceTests(E2EFixture fixture, ITestOutputHelper output)
```

**Why E2EFixture?**
- Tests schema initialization service
- Doesn't need auth/tenant
- **Should use**: `E2EStep1Fixture` (lightweight)

---

### **2. WarehouseTemporalE2ETests.cs**

```csharp
[Collection("E2E")]
public class WarehouseTemporalE2ETests : E2EWithDatabaseTestBase
{
    public WarehouseTemporalE2ETests(E2EFixture fixture, ITestOutputHelper output)
        : base(fixture, output, includeWarehouseTest: true)
```

**Why E2EFixture?**
- Inherits from `E2EWithDatabaseTestBase`
- Base class requires `E2EFixture`
- **Should use**: `E2EStep2Fixture` (needs auth + clean DB)

---

### **3. VersioningE2ETests.cs**

```csharp
[Collection("E2E")]
public class VersioningE2ETests
{
    private readonly E2EFixture _fixture;
    
    public VersioningE2ETests(E2EFixture fixture, ITestOutputHelper output)
```

**Why E2EFixture?**
- Tests versioning system
- Doesn't need auth/tenant
- **Should use**: `E2EStep1Fixture` (lightweight)

---

### **4. TenantCrudTests.cs**

```csharp
[Collection("E2E")]
public class TenantCrudTests
{
    public TenantCrudTests(E2EFixture fixture, ITestOutputHelper output)
```

**Why E2EFixture?**
- Tests tenant CRUD operations
- **Needs auth!**
- **Should use**: `E2EStep2Fixture` (has auth + tenant)

---

### **5. E2EWithDatabaseTestBase.cs** (Critical!)

```csharp
public abstract class E2EWithDatabaseTestBase
{
    protected E2EWithDatabaseTestBase(E2EFixture fixture, ITestOutputHelper output, bool includeWarehouseTest = true)
    {
        _fixture = fixture;
    }
}
```

**Why E2EFixture?**
- Base class for many tests
- Changing this affects ALL derived tests
- **Should use**: `E2EStep2Fixture` (most tests need auth)

**Impact**: 
- Used by: `WarehouseTemporalE2ETests`, `TemporalEdgeCaseTests`, etc.
- **Migration priority**: HIGH (affects many tests)

---

### **6. E2ETestBase.cs** (Critical!)

Contains 2 base classes:

```csharp
// For isolated tests (clear DB each time)
public abstract class IsolatedE2ETestBase
{
    protected IsolatedE2ETestBase(E2EFixture fixture, ITestOutputHelper output)
}

// For shared state tests (reuse DB)
public abstract class SharedStateE2ETestBase
{
    protected SharedStateE2ETestBase(E2EFixture fixture, ITestOutputHelper output)
}
```

**Why E2EFixture?**
- Base classes for test organization
- **Should use**: `E2EStep2Fixture` (most tests need auth)

---

## 🔄 Migration Strategy

### **Phase 1: Update Base Classes** (High Priority)

1. ✅ `E2EWithDatabaseTestBase.cs` → Change to `E2EStep2Fixture`
2. ✅ `E2ETestBase.cs` → Change both base classes to `E2EStep2Fixture`
3. ✅ `E2ETestBootstrapper.cs` → Change to `E2EStep2Fixture`

**Impact**: Automatically migrates ALL derived tests

---

### **Phase 2: Update Collection Definition**

```csharp
// Old
[CollectionDefinition("E2E")]
public class E2ECollection : ICollectionFixture<E2EFixture>
{
}

// New - Option 1: Lightweight
[CollectionDefinition("E2EStep1")]
public class E2EStep1Collection : ICollectionFixture<E2EStep1Fixture>
{
}

// New - Option 2: Full setup
[CollectionDefinition("E2EStep2")]
public class E2EStep2Collection : ICollectionFixture<E2EStep2Fixture>
{
}
```

---

### **Phase 3: Update Individual Tests**

For tests NOT using base classes:

```csharp
// Old
[Collection("E2E")]
public class MyTest
{
    public MyTest(E2EFixture fixture) { }
}

// New
[Collection("E2EStep1")]  // or "E2EStep2"
public class MyTest
{
    public MyTest(E2EStep1Fixture fixture) { }  // or E2EStep2Fixture
}
```

---

## 📊 Migration Priority

| Test/Class | Priority | Reason | Target Fixture |
|------------|----------|--------|----------------|
| **E2EWithDatabaseTestBase** | 🔴 **HIGH** | Affects many tests | E2EStep2Fixture |
| **E2ETestBase** | 🔴 **HIGH** | Affects many tests | E2EStep2Fixture |
| **E2ETestBootstrapper** | 🟡 **MEDIUM** | Helper class | E2EStep2Fixture |
| **TenantCrudTests** | 🟡 **MEDIUM** | Needs auth | E2EStep2Fixture |
| **WarehouseTemporalE2ETests** | 🟡 **MEDIUM** | Needs auth | E2EStep2Fixture |
| **VersioningE2ETests** | 🟢 **LOW** | No auth needed | E2EStep1Fixture |
| **SchemaInitializationServiceTests** | 🟢 **LOW** | No auth needed | E2EStep1Fixture |

---

## 🎯 Recommendation

### **Quick Win: Update Base Classes**

Updating just 3 base classes will automatically migrate ~20+ tests:

1. `E2EWithDatabaseTestBase.cs`
2. `E2ETestBase.cs` (IsolatedE2ETestBase + SharedStateE2ETestBase)
3. `E2ETestBootstrapper.cs`

**Effort**: ~30 minutes  
**Impact**: ~20+ tests migrated  
**Benefit**: Remove `EnsureRegistrySchemaExistsAsync()` overhead

---

### **Long Term: Deprecate E2EFixture**

Once all tests migrated:
1. ✅ Mark `E2EFixture` as `[Obsolete]`
2. ✅ Add warning message
3. ✅ Eventually delete

---

## 🏁 Summary

### **Tests Still Using E2EFixture**: ~13 direct + many via base classes

### **Why?**
1. ⏰ Haven't been migrated yet (written before lazy loading)
2. 🔗 Depend on base classes that use E2EFixture
3. 📦 Use collection fixture with E2EFixture

### **Should Migrate?**
✅ **YES** - To remove `EnsureRegistrySchemaExistsAsync()` overhead and follow "API-only" principle

### **Migration Strategy**:
1. 🔴 **HIGH**: Update base classes (affects most tests)
2. 🟡 **MEDIUM**: Update collection definitions
3. 🟢 **LOW**: Update individual tests

### **Target Fixtures**:
- Tests needing auth/tenant → `E2EStep2Fixture`
- Tests NOT needing auth → `E2EStep1Fixture`

**Rating**: Migration is straightforward, high impact! 🚀
