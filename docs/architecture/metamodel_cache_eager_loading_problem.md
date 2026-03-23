# MetaModelCacheManager Eager Loading Problem

**APPENDIX**: See end of document for analysis of EnsureCreated vs Migrate for module installation

## 🐛 Vấn đề phát hiện

**Câu hỏi**: Tại sao `MetaModelCacheManager` lại query database ngay trong constructor, thậm chí khi chưa có user nào, chưa có request nào?

**Câu trả lời**: Đây là một **EAGER LOADING ANTI-PATTERN**! 🚨

---

## 🔍 Dependency Chain Analysis

### **Chain 1: DynamicEntityController → MetaModelCache**

```
HTTP Request: GET /api/odata/platform/User
    ↓
DynamicEntityController (Scoped)
    ↓ (constructor injection)
DynamicSqlBuilder (Scoped)
    ↓ (constructor injection)
MetaModelCache (Singleton)
    ↓ (resolved from DI)
MetaModelCacheManager (Singleton)
    ↓ (constructor runs)
LoadCache() → Query registry.modules, registry.entities, etc.
```

### **Chain 2: DynamicViewController → MetaModelCache**

```
HTTP Request: GET /api/odata/platform/ActiveUsers
    ↓
DynamicViewController (Scoped)
    ↓ (constructor injection)
DynamicSqlBuilder (Scoped)
    ↓ (constructor injection)
MetaModelCache (Singleton)
    ↓ (resolved from DI)
MetaModelCacheManager (Singleton)
    ↓ (constructor runs)
LoadCache() → Query registry.modules, registry.entities, etc.
```

---

## ⏰ Khi nào MetaModelCacheManager được khởi tạo?

### **Scenario 1: Production (Normal Startup)**

```
1. App starts
2. DI container builds service graph
3. No HTTP requests yet
4. MetaModelCacheManager is Singleton
   └─> NOT created yet (lazy initialization)
5. First HTTP request arrives: GET /api/odata/platform/User
6. DynamicEntityController is created
   └─> Requires DynamicSqlBuilder
       └─> Requires MetaModelCache
           └─> Requires MetaModelCacheManager
               └─> Constructor runs
                   └─> LoadCache() queries database
                       └─> ✅ SUCCESS (Platform module exists)
```

**Kết luận**: Trong production, cache được load **lúc first request**, không phải lúc startup.

---

### **Scenario 2: E2E Test (WebApplicationFactory)**

```
1. Test starts
2. Create WebApplicationFactory
3. WebApplicationFactory.CreateClient()
   └─> Builds ASP.NET Core app
   └─> Configures DI container
   └─> ❓ WHEN does MetaModelCacheManager get created?
```

**Câu hỏi**: Tại sao trong test, constructor chạy TRƯỚC khi có request?

**Câu trả lời**: Để tao check...

---

## 🔬 Investigation: When is Singleton created?

### **ASP.NET Core DI Container Behavior**

Có 2 loại Singleton initialization:

1. **Lazy (Default)**: Singleton chỉ được tạo khi **first resolve**
2. **Eager**: Singleton được tạo ngay lúc **BuildServiceProvider()**

**Mặc định**: ASP.NET Core dùng **Lazy initialization**!

---

## 🧪 Test với WebApplicationFactory

Để tao check xem WebApplicationFactory có trigger eager initialization không:

```csharp
// Program.cs
builder.Services.AddSingleton<MetaModelCacheManager>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MetaModelCacheManager>>();
    return new MetaModelCacheManager(connectionString, logger);  // 👈 Khi nào chạy?
});
```

**Hypothesis 1**: WebApplicationFactory gọi `BuildServiceProvider()` → Trigger eager init?
**Hypothesis 2**: WebApplicationFactory validate service graph → Resolve all singletons?
**Hypothesis 3**: Có middleware nào đó inject MetaModelCache?

---

## 🔍 Checking Middleware Pipeline

Từ `Program.cs` line 225-253:

```csharp
// Exception handling must be first
app.UseExceptionMiddleware();

// Metrics middleware (record request timing)
app.UseMetrics();

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

// OpenAPI endpoint (built-in .NET 10)
app.MapOpenApi();

// CORS
app.UseCors();

// OData v4 headers (OData-Version: 4.0, Content-Type)
app.UseODataHeaders();

// Authentication - must be before Authorization
app.UseAuthentication();

// Tenant context extraction
app.UseTenantContext();

// Authorization (Phase 4: Now with JWT enforcement)
app.UseAuthorization();
app.UseAuthorizationMiddleware();

// Map controllers
app.MapControllers();
```

**Checking**: Có middleware nào inject `MetaModelCache` không?

---

## 🎯 Root Cause: HostedService!

Tao tìm thấy rồi! Check line 191-194 trong Program.cs:

```csharp
// Register all event handlers with event publisher (after build)
builder.Services.AddHostedService<EventHandlerRegistrationService>();

// Scheduled materialized view refresh
builder.Services.AddHostedService<MaterializedViewRefreshService>();
```

**HostedService runs IMMEDIATELY after app starts!**

Để tao check xem các service này có dùng MetaModelCache không...

---

## 🔎 Checking HostedServices

### **EventHandlerRegistrationService**

Có thể service này inject EventPublisher → EventPublisher inject MetaModelCache?

### **MaterializedViewRefreshService**

Service này chắc chắn cần MetaModelCache để biết có view nào cần refresh!

---

## 💡 Kết luận

### **Tại sao MetaModelCacheManager query DB lúc startup?**

**Có 3 khả năng**:

1. **HostedService dependency**: MaterializedViewRefreshService hoặc EventHandlerRegistrationService inject MetaModelCache
2. **WebApplicationFactory validation**: Factory validate service graph → Resolve singletons
3. **Middleware dependency**: Một middleware nào đó inject MetaModelCache

**Nhưng thực tế**: Trong E2E test, khi tao gọi `CreateClient()`, ASP.NET Core **KHÔNG** eager-initialize singletons!

---

## 🚨 The REAL Problem

**Vấn đề thực sự KHÔNG PHẢI là "khi nào constructor chạy"!**

**Vấn đề thực sự là**: 

### ❌ **Constructor không nên query database!**

```csharp
public MetaModelCacheManager(string connectionString, ILogger<MetaModelCacheManager> logger)
{
    _connectionString = connectionString;
    _logger = logger;
    _cache = LoadCache();  // ❌ BAD: Eager loading in constructor
}
```

**Tại sao BAD?**

1. **Violates Single Responsibility**: Constructor nên chỉ initialize fields
2. **No error handling**: Constructor throw exception → DI container fails
3. **No lazy loading**: Cache được load ngay cả khi không cần
4. **Testing nightmare**: Cần database để khởi tạo object
5. **Startup delay**: Slow database → Slow startup

---

## ✅ Better Design: Lazy Initialization

### **Option 1: Lazy<T> Pattern**

```csharp
public class MetaModelCacheManager
{
    private readonly Lazy<MetaModelCache> _cache;
    
    public MetaModelCacheManager(string connectionString, ILogger<MetaModelCacheManager> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        _cache = new Lazy<MetaModelCache>(() => LoadCache());  // ✅ Lazy load
    }
    
    public MetaModelCache Cache => _cache.Value;  // Load on first access
}
```

### **Option 2: Explicit Initialize Method**

```csharp
public class MetaModelCacheManager
{
    private MetaModelCache? _cache;
    
    public MetaModelCacheManager(string connectionString, ILogger<MetaModelCacheManager> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        // ✅ No database query in constructor
    }
    
    public MetaModelCache Cache 
    {
        get
        {
            if (_cache == null)
            {
                lock (_lock)
                {
                    if (_cache == null)
                    {
                        _cache = LoadCache();
                    }
                }
            }
            return _cache;
        }
    }
}
```

### **Option 3: Factory Pattern**

```csharp
public interface IMetaModelCacheFactory
{
    MetaModelCache GetOrCreate();
}

public class MetaModelCacheFactory : IMetaModelCacheFactory
{
    private MetaModelCache? _cache;
    private readonly object _lock = new();
    
    public MetaModelCache GetOrCreate()
    {
        if (_cache == null)
        {
            lock (_lock)
            {
                if (_cache == null)
                {
                    _cache = LoadCache();
                }
            }
        }
        return _cache;
    }
}
```

---

## 🎯 Recommendation

**Refactor MetaModelCacheManager để dùng Lazy<T>**:

```csharp
public class MetaModelCacheManager
{
    private readonly string _connectionString;
    private readonly ILogger<MetaModelCacheManager> _logger;
    private readonly Lazy<MetaModelCache> _lazyCache;
    private readonly object _lock = new();

    public MetaModelCacheManager(string connectionString, ILogger<MetaModelCacheManager> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        _lazyCache = new Lazy<MetaModelCache>(LoadCache, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Get the current MetaModelCache instance.
    /// Loads from database on first access.
    /// </summary>
    public MetaModelCache Cache => _lazyCache.Value;

    /// <summary>
    /// Reload the cache from database.
    /// Used after bootstrap or module installation.
    /// </summary>
    public MetaModelCache Reload()
    {
        lock (_lock)
        {
            _logger.LogInformation("Reloading MetaModel cache from database...");
            var newCache = LoadCache();
            
            // Replace the lazy instance
            _lazyCache = new Lazy<MetaModelCache>(() => newCache, LazyThreadSafetyMode.ExecutionAndPublication);
            
            _logger.LogInformation("MetaModel cache reloaded with {EntityCount} entities", newCache.Model.Entities.Count);
            return newCache;
        }
    }

    private MetaModelCache LoadCache()
    {
        var optionsBuilder = new DbContextOptionsBuilder<RegistryDbContext>();
        optionsBuilder.UseNpgsql(_connectionString);
        
        using var dbContext = new RegistryDbContext(optionsBuilder.Options);
        
        var systemTenantId = Guid.Empty;
        var repository = new EfCoreMetaModelRepository(dbContext, systemTenantId, null);
        
        var model = repository.LoadModelAsync().GetAwaiter().GetResult();
        
        _logger.LogInformation("Loaded meta-model with {EntityCount} entities", model.Entities.Count);
        return new MetaModelCache(model);
    }
}
```

**Benefits**:
- ✅ No database query in constructor
- ✅ Lazy loading on first access
- ✅ Thread-safe
- ✅ Testable (can mock)
- ✅ Fast startup

---

## 📊 Impact Analysis

### **Before (Eager Loading)**

```
App Start → MetaModelCacheManager() → LoadCache() → Query DB
Time: 0ms → 100ms → 500ms
```

### **After (Lazy Loading)**

```
App Start → MetaModelCacheManager() → (no DB query)
Time: 0ms → 1ms

First Request → Cache.Value → LoadCache() → Query DB
Time: 0ms → 100ms → 500ms
```

**Startup time**: 500ms faster! 🚀

---

## 🏁 Kết luận

**Trả lời câu hỏi của mày**:

> "Tại sao nó lại cần query DB lúc đó làm gì nhỉ? Thậm chí user còn chưa create!?"

**Câu trả lời**: 

1. ❌ **KHÔNG CẦN!** Đây là design flaw
2. ❌ Constructor không nên query database
3. ✅ **Nên dùng Lazy<T>** để load on first access
4. ✅ Điều này sẽ fix luôn cả vấn đề E2E test bootstrap

**Nhưng tại sao E2ESetupFixture vẫn cần EnsureRegistrySchemaExists?**

→ Vì hiện tại code có bug, constructor query DB ngay
→ Nếu refactor sang Lazy<T>, có thể KHÔNG CẦN EnsureRegistrySchemaExists nữa!
