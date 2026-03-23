---
description: Thêm ERP module mới vào library
---

# Add New ERP Module

Thêm một module mới vào thư viện ERP của BMMDL.

## Current Module Structure
```
erp_modules/
├── 00_platform/      # Module 0: Tenant, User, Role, Permission
├── 01_core/          # Foundation types, aspects
├── 02_master_data/   # Employee, Customer, Product
├── 03_hr/            # Leave, Performance, Training
├── 04_finance/       # Payroll configuration
├── 05_scm/           # Sales, Purchasing, Inventory
├── 06_rules/         # Business validations
├── 07_services/      # API service definitions
├── 08_security/      # Access control policies
└── 09_workflow/      # Process automation
```

## Steps

### 1. Tạo thư mục module
// turbo
```powershell
$moduleName = "10_your_module"
New-Item -ItemType Directory -Path "erp_modules/$moduleName" -Force
```

### 2. Tạo file module.bmmdl
```powershell
@"
// Module declaration
module YourModule version '1.0.0' {
    author: 'Your Name';
    description: 'Description of your module';
    
    // Declare dependencies
    depends on CoreFoundation version '>=1.0.0';
    depends on MasterData version '>=1.0.0';
    
    // Import namespaces from dependencies
    imports Foundation;
    imports MasterData;
    
    // Publish your namespace
    publishes YourDomain;
}

namespace YourDomain;

// Use aspects from Foundation
using Foundation.TenantAware;
using Foundation.Auditable;

// Define your entities
entity YourEntity : TenantAware, Auditable {
    key ID: UUID;
    name: String(100);
    description: String?;
    
    // Association to master data
    customer: Association to Customer;
    
    // Computed field
    displayName: String computed name + ' (' + ID.ToString() + ')';
}
"@ | Out-File -FilePath "erp_modules/$moduleName/module.bmmdl" -Encoding UTF8
```

### 3. Compile và Publish via API

Dùng Registry.Api endpoint để compile và publish:

```powershell
# Đọc nội dung BMMDL file
$bmmdlSource = Get-Content -Path "erp_modules/10_your_module/module.bmmdl" -Raw

# POST to Admin API
$body = @{
    bmmdlSource = $bmmdlSource
    moduleName = "YourModule"
    publish = $true
    initSchema = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5001/api/admin/compile" `
    -Method POST `
    -Body $body `
    -ContentType "application/json"
```

### 4. Verify trong database
```sql
-- Check module registered
SELECT "Name", "Version", "Status" FROM "Modules" WHERE "Name" = 'YourModule';

-- Check entities created
SELECT "Name", "QualifiedName" FROM "Entities" WHERE "ModuleId" = (
    SELECT "Id" FROM "Modules" WHERE "Name" = 'YourModule'
);
```

## Module Declaration Syntax Reference

```bmmdl
module ModuleName version 'X.Y.Z' {
    author: 'Author Name';                    // Required
    description: 'Module description';        // Required
    
    depends on OtherModule version '>=1.0.0'; // Dependency with semver
    depends on AnotherModule version '^2.0';  // Caret range
    
    imports Namespace1;                       // Import from dependencies
    imports Namespace2;
    
    publishes MyNamespace;                    // Namespace this module exports
}
```

## Dependency Chain Rules

1. Modules phải khai báo dependencies trước khi import namespace từ chúng
2. API tự động resolve transitive dependencies khi compile
3. Version constraints sử dụng SemVer: `>=`, `^`, `~`
