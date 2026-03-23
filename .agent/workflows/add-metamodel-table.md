---
description: Thêm table mới vào MetaModel database schema
---

# Add MetaModel Table

Thêm bảng mới vào MetaModel database schema (Layer 1).

## Context
MetaModel tables lưu trữ metadata của BMMDL (entities, fields, expressions, etc.) trong PostgreSQL.

**Project:** `src/BMMDL.MetaModel.Api`
**DbContext:** `Data/MetaModelDbContext.cs`

## Steps

### 1. Tạo Entity model
Tạo file `src/BMMDL.MetaModel.Api/Data/Entities/YourEntity.cs`:
```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BMMDL.MetaModel.Api.Data.Entities;

[Table("your_entities")]
public class YourEntity
{
    [Key]
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(1000)]
    public string? Description { get; set; }
    
    // Foreign key
    public Guid? ParentId { get; set; }
    
    [ForeignKey(nameof(ParentId))]
    public Entity? Parent { get; set; }
    
    // Multi-tenancy
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    
    // Audit fields
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

### 2. Thêm DbSet vào DbContext
Mở `src/BMMDL.MetaModel.Api/Data/MetaModelDbContext.cs`:
```csharp
public DbSet<YourEntity> YourEntities => Set<YourEntity>();
```

### 3. Configure entity trong OnModelCreating
```csharp
modelBuilder.Entity<YourEntity>(entity =>
{
    entity.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
    
    entity.HasOne(e => e.Parent)
        .WithMany()
        .HasForeignKey(e => e.ParentId)
        .OnDelete(DeleteBehavior.Restrict);
});
```

### 4. Tạo EF Core migration
// turbo
```powershell
dotnet ef migrations add AddYourEntity --project src/BMMDL.MetaModel.Api
```

### 5. Review migration script
// turbo
```powershell
dotnet ef migrations script --project src/BMMDL.MetaModel.Api -o artifacts/migration_script.sql
```

### 6. Apply migration (tự động khi start API)
```powershell
# Option 1: Set environment variable
$env:ApplyMigrations = "true"
dotnet run --project src/BMMDL.MetaModel.Api

# Option 2: Manual apply
dotnet ef database update --project src/BMMDL.MetaModel.Api
```

### 7. Thêm Repository methods
Mở `src/BMMDL.Registry/Persistence/EfCoreMetaModelRepository.cs`:
```csharp
public async Task SaveYourEntityAsync(YourEntity entity)
{
    await _context.YourEntities.AddAsync(entity);
    await _context.SaveChangesAsync();
}
```

### 8. Test
// turbo
```powershell
dotnet test src/BMMDL.Tests --filter "FullyQualifiedName~MetaModel" --logger "trx;LogFileName=metamodel_tests.trx" --results-directory artifacts
```

## Naming Conventions

| C# | Database |
|----|----------|
| `YourEntity` | `your_entities` |
| `ParentId` | `parent_id` |
| `CreatedAt` | `created_at` |

## Common Patterns

### Multi-tenancy (required for all tables)
```csharp
public Guid TenantId { get; set; }
[ForeignKey(nameof(TenantId))]
public Tenant Tenant { get; set; } = null!;
```

### Audit fields
```csharp
public DateTime CreatedAt { get; set; }
public DateTime? UpdatedAt { get; set; }
public string? CreatedBy { get; set; }
```

### Soft delete
```csharp
public bool IsDeleted { get; set; }
public DateTime? DeletedAt { get; set; }
```
