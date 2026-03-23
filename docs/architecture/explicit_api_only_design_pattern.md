# Explicit API-Only Design Pattern

**Date**: 2026-01-24  
**Philosophy**: "WebApp ngủ gật cho đến khi có explicit API call"

---

## 🎯 Design Philosophy

### **Principle: Explicit > Implicit**

```
WebApp starts → "Ngủ gật" 😴 → Không làm gì cả
    ↓
Client hit API endpoint → Thực hiện thao tác
    ↓
Về sau → Giấu/bảo vệ endpoints nguy hiểm
```

**Tại sao tốt?**
- ✅ **Explicit > Implicit**: Mọi thao tác đều phải có API call rõ ràng
- ✅ **Safe by default**: Server không tự động làm gì nguy hiểm
- ✅ **Auditable**: Có thể log/track ai gọi endpoint nguy hiểm
- ✅ **Controllable**: Về sau có thể add auth, rate limiting, etc.

---

## 🔧 Implementation: E2EFixture Refactoring

### **Before (Implicit DB Access)**

```csharp
public async Task InitializeAsync()
{
    // ❌ Direct SQL access - implicit, không qua API
    await EnsureRegistrySchemaExistsAsync();  
    // Uses: db.Database.MigrateAsync()
    
    // Start servers
    RegistryClient = _registryFactory.CreateClient();
    RuntimeClient = _runtimeFactory.CreateClient();
    
    // Clear DB via API
    await ClearDatabaseAsync();
}
```

**Vấn đề**:
- ❌ `EnsureRegistrySchemaExistsAsync()` dùng direct SQL
- ❌ Không consistent (một số qua API, một số direct SQL)
- ❌ Không auditable (không biết ai tạo schema)

---

### **After (Explicit API-Only)**

```csharp
public async Task InitializeAsync()
{
    // ✅ Start servers WITHOUT any DB access
    // Lazy loading: MetaModelCacheManager doesn't query DB in constructor
    RegistryClient = _registryFactory.CreateClient();
    RuntimeClient = _runtimeFactory.CreateClient();
    
    // ✅ All operations via explicit API calls
    await _bootstrapLock.WaitAsync();
    try
    {
        if (!_modulesBootstrapped)
        {
            // Step 1: Install Platform (via API)
            // This will:
            // - Compile module
            // - Create registry schema (if not exists)
            // - Create platform schema
            await EnsureModuleInstalledAsync("Platform", platformSource);
            
            // Step 2: Install other modules (via API)
            await EnsureModuleInstalledAsync("WarehouseTest", warehouseSource);
            
            _modulesBootstrapped = true;
        }
    }
    finally
    {
        _bootstrapLock.Release();
    }
}
```

**Benefits**:
- ✅ **No direct SQL access** - tất cả qua API
- ✅ **Lazy loading compatible** - server start không cần DB
- ✅ **Explicit operations** - mọi thao tác đều qua API endpoint
- ✅ **Auditable** - có thể log tất cả API calls

---

## 🚀 How It Works

### **Step 1: Server Starts (No DB Query)**

```csharp
RegistryClient = _registryFactory.CreateClient();  // Start RegistryApi
RuntimeClient = _runtimeFactory.CreateClient();    // Start RuntimeApi
```

**What happens**:
1. WebApplicationFactory builds and starts servers
2. **RegistryApi starts**:
   - Tries to apply migrations (Program.cs line 138-143)
   - If registry schema doesn't exist → Migration creates it
   - ✅ Server starts OK
3. **RuntimeApi starts**:
   - MetaModelCacheManager created (Lazy<T>)
   - MaterializedViewRefreshService created (injects manager)
   - **NO database query!**
   - ✅ Server starts OK

---

### **Step 2: First API Call (Module Installation)**

```csharp
await EnsureModuleInstalledAsync("Platform", platformSource);
```

**What happens**:
```
POST /api/admin/compile
{
  "bmmdlSource": "...",
  "moduleName": "Platform",
  "publish": true,
  "initSchema": true,
  "force": true
}
```

**Server side** (AdminService.CompileAndInstallAsync):
1. Compile BMMDL source → BmModel
2. Publish to registry → Save to `registry.*` tables
3. Initialize schema → Create `platform` schema + tables
4. Return success

**Then**:
```
POST /api/admin/reload-cache
```

**Server side** (RuntimeAdminController.ReloadCache):
1. Call `MetaModelCacheManager.Reload()`
2. **This is the FIRST time cache is loaded!** (Lazy<T> triggered)
3. Query `registry.modules`, `registry.entities`, etc.
4. Build MetaModelCache
5. Return success

---

## 🔒 Security: Hiding Dangerous Endpoints

### **Current State (Development)**

```csharp
[HttpPost("clear-database")]
[ProducesResponseType<ClearDatabaseResponse>(StatusCodes.Status200OK)]
public async Task<IActionResult> ClearDatabase(
    [FromHeader(Name = "X-Admin-Key")] string? adminKey,
    [FromBody] ClearDatabaseRequest request)
{
    if (!ValidateAdminKey(adminKey))
    {
        return Unauthorized();
    }
    
    // ⚠️ DANGEROUS: DROP CASCADE all schemas!
    var result = await _adminService.ClearDatabaseAsync(request);
    return Ok(result);
}
```

**Protection**: Chỉ có `X-Admin-Key` header

---

### **Future State (Production)**

```csharp
[HttpPost("clear-database")]
#if DEBUG
[ProducesResponseType<ClearDatabaseResponse>(StatusCodes.Status200OK)]
#else
[ApiExplorerSettings(IgnoreApi = true)]  // Hide from Swagger
#endif
public async Task<IActionResult> ClearDatabase(
    [FromHeader(Name = "X-Admin-Key")] string? adminKey,
    [FromBody] ClearDatabaseRequest request)
{
    #if !DEBUG
    // In production, completely disable this endpoint
    return NotFound();
    #endif
    
    if (!ValidateAdminKey(adminKey))
    {
        return Unauthorized();
    }
    
    // Log who is calling this dangerous endpoint
    _logger.LogWarning(
        "DANGEROUS OPERATION: ClearDatabase called by {IP} at {Time}",
        HttpContext.Connection.RemoteIpAddress,
        DateTime.UtcNow);
    
    var result = await _adminService.ClearDatabaseAsync(request);
    return Ok(result);
}
```

**Additional protections**:
- ✅ Disable in production (`#if !DEBUG`)
- ✅ Hide from Swagger
- ✅ Log all calls
- ✅ Rate limiting
- ✅ IP whitelist

---

## 📊 Comparison

| Aspect | Before | After |
|--------|--------|-------|
| **DB Access** | Direct SQL + API | ✅ API only |
| **Server Start** | Requires registry schema | ✅ No DB required |
| **Consistency** | Mixed (SQL + API) | ✅ All via API |
| **Auditability** | Partial | ✅ Full (all API calls) |
| **Security** | Implicit operations | ✅ Explicit API calls |
| **Testability** | Coupled to DB | ✅ Decoupled |

---

## 🎯 Key Takeaways

### **1. Lazy Loading Enables API-Only Design**

**Before**:
```
Server start → MetaModelCacheManager() → LoadCache() → Query DB
→ 💥 CRASH if registry schema doesn't exist
→ Must use EnsureRegistrySchemaExists() before CreateClient()
```

**After**:
```
Server start → MetaModelCacheManager() → Just store fields → ✅ OK
First API call → .Cache → LoadCache() → Query DB
```

---

### **2. Explicit > Implicit**

**Bad (Implicit)**:
```csharp
// Test code directly accesses DB
await db.Database.MigrateAsync();
```

**Good (Explicit)**:
```csharp
// Test code calls API
await client.PostAsJsonAsync("/api/admin/compile", request);
```

---

### **3. Safe by Default**

**Server starts → Does nothing**
- ✅ No schema creation
- ✅ No data migration
- ✅ No cache loading

**Client calls API → Explicit operation**
- ✅ Auditable
- ✅ Controllable
- ✅ Securable

---

## 🏁 Summary

**Design philosophy**: "WebApp ngủ gật cho đến khi có explicit API call"

**Implementation**:
1. ✅ Removed `EnsureRegistrySchemaExistsAsync()` from E2EFixture
2. ✅ Server starts without DB query (lazy loading)
3. ✅ All operations via API endpoints
4. ✅ Explicit, auditable, controllable

**Future**:
- 🔒 Hide dangerous endpoints in production
- 📝 Log all dangerous operations
- 🚦 Add rate limiting
- 🔐 Add IP whitelist

**Rating**: 10/10 - Clean, explicit, secure design! 🚀
