# E2ESetupFixture Analysis Report

**File**: `BMMDL.Tests.E2E/Integration/Api/E2ESetupFixture.cs`  
**Lines**: 528  
**Complexity**: ⭐⭐⭐⭐⭐⭐⭐⭐ (8/10 - Extremely Complex)

---

## 🎯 Mục đích chính

`E2ESetupFixture` là một **xUnit IAsyncLifetime fixture** được thiết kế để:
1. **Chuẩn bị môi trường E2E test hoàn chỉnh** từ zero-state
2. **Tái sử dụng setup** giữa các tests để tăng tốc độ
3. **Quản lý lifecycle** của 2 WebApplicationFactory (Registry API + Runtime API)

---

## 🏗️ Kiến trúc tổng quan

### 1. **Dual WebApplicationFactory Pattern**

```csharp
private readonly RegistryApiWebFactory _registryFactory;  // Port 5001 - Admin/Compile
private readonly RuntimeApiWebFactory _runtimeFactory;    // Port 5000 - OData/Runtime
```

**Tại sao cần 2 factories?**
- **Registry API**: Compile modules, admin operations (clear DB, bootstrap)
- **Runtime API**: User authentication, OData queries, business logic

### 2. **Static Singleton Pattern với Locking**

```csharp
private static readonly SemaphoreSlim _initLock = new(1, 1);
private static bool _initialized = false;
private static string _staticAccessToken = "";
// ... cached state
```

**Mục đích**: 
- ✅ **Chỉ bootstrap 1 lần** cho toàn bộ test suite
- ✅ **Thread-safe** khi chạy parallel tests
- ✅ **Tái sử dụng** auth token, tenant ID giữa các tests

### 3. **Smart Re-initialization Detection**

```csharp
var platformExists = await PlatformSchemaExistsAsync();

if (!_initialized || !platformExists)
{
    // Re-bootstrap nếu schema bị xóa bởi test khác
    if (_initialized && !platformExists)
    {
        Console.WriteLine("⚠️ Platform schema was cleared, re-initializing...");
        _initialized = false;
        AuthHelper.ClearCache();
    }
    // ... run full setup
}
```

**Clever!** Fixture detect được khi database bị clear và tự động re-initialize.

---

## 🔄 Quy trình Bootstrap (4 bước)

### **Step 0: Ensure Registry Schema Exists** ⚠️ CRITICAL

```csharp
await EnsureRegistrySchemaExistsAsync();
```

**Vấn đề được giải quyết**:
- WebApplicationFactory khởi động server → Server query `registry.modules` ngay lập tức
- Nếu registry schema chưa tồn tại → **Startup fails BEFORE** ta có thể gọi `/api/admin/clear-database`
- **Giải pháp**: Dùng EF Core `MigrateAsync()` để tạo registry schema TRƯỚC KHI tạo clients

```csharp
await using var db = new RegistryDbContext(options);
await db.Database.MigrateAsync();  // ✅ Idempotent, safe to run multiple times
```

**Đây là pattern "Fail-Fast Prevention"** - rất quan trọng!

---

### **Step 1: Clear Database**

```csharp
POST /api/admin/clear-database
{
    clearRegistry: true,
    dropSchemas: true,
    schemas: null  // Drop ALL schemas
}
```

**Làm gì?**
- Drop tất cả schemas (platform, warehouse, etc.)
- Clear registry tables (modules, entities, fields, etc.)
- Reset về **zero-state**

---

### **Step 2: Bootstrap Platform Module**

```csharp
POST /api/admin/compile
{
    bmmdlSource: _module0Source,
    moduleName: "Platform",
    tenantId: Guid.Empty,
    publish: true,
    initSchema: true,
    force: true
}
```

**Làm gì?**
1. Compile `00_platform/module.bmmdl`
2. Publish vào registry
3. Generate và execute DDL → Tạo `platform` schema
4. Reload runtime cache

**Module 0 (Platform)** chứa:
- `platform.User`
- `platform.Tenant`
- `platform.Role`
- `platform.Permission`
- Core authentication/authorization entities

---

### **Step 3: Create User & Login**

```csharp
await AuthHelper.AuthenticateAsync(RuntimeClient);
```

**Làm gì?**
- Register user mới với username pattern `e2e_test_{guid}`
- Login và lấy JWT access token
- Set `Authorization: Bearer {token}` header cho RuntimeClient

**Note**: Dùng `AuthHelper` - một helper class đã được verify hoạt động tốt.

---

### **Step 4: Create Tenant**

```csharp
POST /api/odata/platform/Tenant
{
    code: "E2E_{guid}",
    name: "E2E Test Tenant {guid}",
    subscriptionTier: "enterprise",
    maxUsers: 100
}
```

**Làm gì?**
- Tạo tenant mới cho user
- Cache `TenantId` để dùng trong tests

---

## 🎭 Các Pattern đặc biệt

### 1. **Defensive Schema Check**

```csharp
private async Task<bool> PlatformSchemaExistsAsync()
{
    // Query information_schema để check platform.platform_user table
    var sql = "SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
              "WHERE table_schema = 'platform' AND table_name = 'platform_user')";
}
```

**Tại sao?** Detect khi test khác đã clear database → Re-initialize.

---

### 2. **Module Path Resolution**

```csharp
var paths = new[]
{
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "..", "erp_modules", "00_platform", "module.bmmdl"),
    @"E:\Mudop\erp_modules\00_platform\module.bmmdl"
};
```

**Fallback strategy**: Thử relative path từ test assembly, fallback về absolute path.

---

### 3. **Delay for Database Settling**

```csharp
await Task.Delay(500);  // Wait for DB to settle
```

**Tại sao?** PostgreSQL async operations cần thời gian để commit/propagate.

---

## 🔧 Helper Methods

### `InstallModuleAsync(modulePath, moduleName)`

Compile và install module bổ sung sau khi bootstrap (ví dụ: Warehouse module cho temporal tests).

### `CreateAuthenticatedRuntimeClient()`

Tạo HttpClient mới với auth token đã cached.

---

## ⚠️ Vấn đề tiềm ẩn

### 1. **Static State Pollution**

```csharp
private static bool _initialized = false;
private static string _staticAccessToken = "";
```

**Vấn đề**: Static state được share giữa TẤT CẢ test instances.
- Nếu test A clear database → Test B vẫn dùng cached token cũ → **Fail**
- **Giải pháp hiện tại**: Check `PlatformSchemaExistsAsync()` và re-initialize

### 2. **Race Condition Risk**

```csharp
await _initLock.WaitAsync();
```

**Giảm thiểu**: Dùng SemaphoreSlim để serialize initialization.

**Nhưng**: Nếu test chạy parallel và test A đang bootstrap, test B phải đợi → **Slow**

### 3. **Hard-coded Paths**

```csharp
@"E:\Mudop\erp_modules\00_platform\module.bmmdl"
```

**Vấn đề**: Không portable, chỉ work trên máy dev cụ thể.

### 4. **No Cleanup**

```csharp
public Task DisposeAsync()
{
    // Chỉ dispose clients, KHÔNG clear database
}
```

**Vấn đề**: Database state được giữ lại sau khi tests chạy xong.
- ✅ **Good**: Tests chạy nhanh hơn (không cần re-bootstrap)
- ❌ **Bad**: Có thể gây pollution nếu test fail và để lại dirty state

---

## 📊 Performance Characteristics

### **First Run** (Cold Start)
1. EnsureRegistrySchema: ~200ms (migrations)
2. ClearDatabase: ~500ms
3. BootstrapPlatform: ~2-3s (compile + DDL + cache reload)
4. CreateUser: ~500ms
5. CreateTenant: ~200ms

**Total**: ~4-5 seconds

### **Subsequent Runs** (Warm)
- Check `_initialized` → Use cached state
- **Total**: ~50ms (chỉ restore cached values)

---

## 🎯 Kết luận

### ✅ **Điểm mạnh**

1. **Comprehensive Setup**: Tạo môi trường E2E hoàn chỉnh từ zero
2. **Performance Optimization**: Singleton pattern giảm thời gian setup
3. **Defensive Programming**: Detect và recover từ schema changes
4. **Separation of Concerns**: Tách rõ Registry vs Runtime APIs
5. **Fail-Fast Prevention**: Ensure registry schema trước khi start server

### ❌ **Điểm yếu**

1. **High Complexity**: 528 lines, khó maintain
2. **Static State**: Có thể gây race conditions
3. **Hard-coded Values**: Paths, connection strings không configurable
4. **No Isolation**: Tests share database state
5. **Hidden Dependencies**: Phụ thuộc vào `AuthHelper`, `RegistryDbContext`

### 🔮 **Recommendations**

1. **Refactor thành Builder Pattern**:
   ```csharp
   var fixture = new E2EFixtureBuilder()
       .WithCleanDatabase()
       .WithPlatformModule()
       .WithAuthenticatedUser()
       .WithTenant()
       .Build();
   ```

2. **Extract Configuration**:
   ```csharp
   public class E2EConfig
   {
       public string ConnectionString { get; set; }
       public string ModulePath { get; set; }
       public bool ReuseState { get; set; }
   }
   ```

3. **Add Isolation Levels**:
   - **Level 1**: Shared database (current)
   - **Level 2**: Unique schema per test class
   - **Level 3**: Unique database per test

4. **Improve Diagnostics**:
   ```csharp
   public class E2ESetupDiagnostics
   {
       public TimeSpan InitializationTime { get; set; }
       public bool UsedCache { get; set; }
       public List<string> ExecutedSteps { get; set; }
   }
   ```

---

## 🏆 Verdict

**E2ESetupFixture là một "necessary evil"**:
- Cần thiết để test E2E scenarios
- Phức tạp nhưng được thiết kế tốt
- Có thể cải thiện bằng refactoring

**Rating**: 7/10 - Good but can be better! 🚀
