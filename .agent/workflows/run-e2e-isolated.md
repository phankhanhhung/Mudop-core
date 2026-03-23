---
description: Chạy E2E tests với full isolation (mỗi test tự bootstrap riêng)
---

# E2E Isolated Testing Workflow

Tất cả E2E tests đã được migrate sang pattern `IsolatedE2ETestBase` - mỗi test tự động:
1. Clear database
2. Bootstrap Platform module
3. Bootstrap WarehouseTest module (nếu cần)
4. Tạo user mới và login
5. Tạo tenant mới

## Chạy Tests

// turbo-all

### Chạy tất cả isolated tests
```bash
dotnet test src\BMMDL.Tests.E2E --filter "Category=Isolated" --logger "trx;LogFileName=e2e_isolated.trx" --results-directory artifacts
```

### Chạy một test class cụ thể
```bash
dotnet test src\BMMDL.Tests.E2E --filter "FullyQualifiedName~TenantCrudTests"
```

### Chạy một test method cụ thể
```bash
dotnet test src\BMMDL.Tests.E2E --filter "FullyQualifiedName~TenantCrudTests.Create_ShouldReturn201"
```

## Test Infrastructure Files

| File | Mô tả |
|------|-------|
| `E2ETestBootstrapper.cs` | Core bootstrapper - thực hiện full bootstrap sequence |
| `E2ETestBase.cs` | Base classes: `IsolatedE2ETestBase`, `SharedStateE2ETestBase` |
| `E2EFixture.cs` | xUnit fixture - quản lý HttpClient và WebApplicationFactory |
| `AuthHelper.cs` | Helper cho authentication |

## Cách sử dụng trong test class mới

```csharp
using Xunit;
using Xunit.Abstractions;

namespace BMMDL.Tests.Integration.Api;

public class MyNewTests : IsolatedE2ETestBase
{
    public MyNewTests(E2EFixture fixture, ITestOutputHelper output) 
        : base(fixture, output, includeWarehouseTest: true) // false nếu không cần Warehouse
    {
    }
    
    [Fact]
    [Trait("Category", "Isolated")]
    public async Task MyTest()
    {
        // RuntimeClient đã authenticated
        // TestTenantId, TestUserId sẵn sàng
        var response = await RuntimeClient.GetAsync("/api/odata/platform/Tenant");
        // ...assertions...
    }
}
```

## Lưu ý
- Mỗi test method chạy với database sạch hoàn toàn
- Tests chạy tuần tự (parallelizeTestCollections: false)
- Timeout mặc định: 120 giây
- Database connection: localhost:5432, user: bmmdl, db: bmmdl_registry
