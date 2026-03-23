# RegistryDrivenSchemaInitTests Migration Fix - Summary

**Date**: 2026-01-24  
**Issue**: Test failing vì E2E bootstrap clears database  
**Solution**: Chuyển từ E2EWithDatabaseTestBase về DatabaseIntegrationTestBase

---

## ❌ Problem

### **Error**
```
Npgsql.PostgresException : 42P01: relation "modules" does not exist
```

### **Root Cause**

**Flow gây lỗi:**
```
1. E2EWithDatabaseTestBase.InitializeAsync()
   ↓
2. base.InitializeAsync() (IsolatedE2ETestBase)
   ↓
3. Bootstrapper.BootstrapAsync()
   ↓
4. ClearDatabaseAsync() ❌ CLEARS ALL TABLES!
   ↓
5. OnDatabaseConnectedAsync()
   ↓
6. EnsureTestDataExistsAsync()
   ↓
7. SELECT COUNT(*) FROM modules ❌ TABLE KHÔNG TỒN TẠI!
```

**Vấn đề:**
- `E2EWithDatabaseTestBase` kế thừa `IsolatedE2ETestBase`
- `IsolatedE2ETestBase` **CLEARS DATABASE** trong bootstrap
- `RegistryDrivenSchemaInitTests` **CẦN** registry tables (modules, entities, etc.)
- Conflict! ❌

---

## ✅ Solution

### **Chuyển về DatabaseIntegrationTestBase**

Test này **KHÔNG CẦN** E2E features (user, login, tenant, API calls).  
Test này **CHỈ CẦN** database access để test registry-driven schema init.

**Before:**
```csharp
[Collection("E2E")]
public class RegistryDrivenSchemaInitTests : E2EWithDatabaseTestBase
{
    public RegistryDrivenSchemaInitTests(E2EFixture fixture, ITestOutputHelper output)
        : base(fixture, output, includeWarehouseTest: false)
    {
    }
}
```

**After:**
```csharp
[Trait("Category", "Integration")]
public class RegistryDrivenSchemaInitTests : DatabaseIntegrationTestBase
{
    private static readonly Guid TestTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    
    protected override async Task OnDatabaseConnectedAsync()
    {
        await EnsureTestDataExistsAsync();
    }
}
```

---

## 🔧 Changes Made

### 1. **Base Class Change**
```diff
- [Collection("E2E")]
- public class RegistryDrivenSchemaInitTests : E2EWithDatabaseTestBase
+ [Trait("Category", "Integration")]
+ public class RegistryDrivenSchemaInitTests : DatabaseIntegrationTestBase
```

### 2. **Removed E2E Dependencies**
```diff
- using BMMDL.Tests.Integration.Api;
- using Xunit.Abstractions;
+ using BMMDL.Tests.Integration;

- public RegistryDrivenSchemaInitTests(E2EFixture fixture, ITestOutputHelper output)
-     : base(fixture, output, includeWarehouseTest: false)
```

### 3. **Added TestTenantId**
```csharp
+ private static readonly Guid TestTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
```

### 4. **Re-added Helper Methods**
```csharp
+ private async Task<bool> TableExistsAsync(string tableName)
+ private async Task<int> GetColumnCountAsync(string tableName)
```

(Các methods này đã bị xóa vì tưởng duplicate với base class, nhưng `DatabaseIntegrationTestBase` không có!)

---

## 📊 Comparison

### **E2EWithDatabaseTestBase** (Wrong Choice)
```
✅ Has: User, Login, Tenant, API clients, Database connection
❌ Problem: Clears database in bootstrap
❌ Use case: Tests needing BOTH E2E + Database
```

### **DatabaseIntegrationTestBase** (Correct Choice)
```
✅ Has: Database connection only
✅ Benefit: Doesn't clear database
✅ Use case: Tests needing ONLY database access
```

---

## 🎯 When to Use Which Base Class

### **DatabaseIntegrationTestBase**
Use when test needs:
- ✅ Direct SQL queries
- ✅ Database schema operations
- ✅ Registry data already exists
- ❌ NO E2E features needed

**Examples:**
- PostgresSchemaReaderTests
- MigrationExecutorTests
- RegistryDrivenSchemaInitTests ✅

### **E2EWithDatabaseTestBase**
Use when test needs:
- ✅ User/Login/Tenant
- ✅ Authenticated API calls
- ✅ Direct SQL queries
- ✅ Fresh database per test

**Examples:**
- Tests combining API + Database operations
- Tests needing both authenticated requests AND raw SQL

### **IsolatedE2ETestBase**
Use when test needs:
- ✅ User/Login/Tenant
- ✅ Authenticated API calls
- ❌ NO direct SQL needed

**Examples:**
- TenantCrudTests
- IsolatedTenantTests

---

## ✅ Build Status

```
✅ Build succeeded
✅ 0 errors
⚠️  2 warnings (unrelated)
```

---

## 📝 Lessons Learned

### **Wrong Assumption**
```
❌ "Test cần database → Dùng E2EWithDatabaseTestBase"
```

### **Correct Analysis**
```
✅ Test cần GÌ?
   - Cần E2E (user/login/tenant/API)? → E2EWithDatabaseTestBase
   - Chỉ cần database? → DatabaseIntegrationTestBase
   - Cần registry data sẵn có? → DatabaseIntegrationTestBase
```

### **Key Question**
```
"Test này có CẦN database được CLEAR không?"

- YES → E2EWithDatabaseTestBase (fresh DB per test)
- NO → DatabaseIntegrationTestBase (preserve existing data)
```

---

## 🎉 Conclusion

**Issue resolved!**

- ✅ Chuyển từ E2EWithDatabaseTestBase → DatabaseIntegrationTestBase
- ✅ Test không còn clear database
- ✅ Registry tables được preserve
- ✅ Build successful

**Đúng base class cho đúng use case!** 🚀
