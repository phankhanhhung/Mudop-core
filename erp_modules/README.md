# ERP Modules

A modular ERP system built with BMMDL, organized as 8 installable modules with explicit dependencies.

## Module Structure

| # | Module | Description | Dependencies |
|---|--------|-------------|--------------|
| 01 | **core** | Foundation types, aspects, enums | _(none)_ |
| 02 | **master_data** | Employee, Customer, Product, Vendor, Warehouse | core |
| 03 | **hr** | Leave, Performance, Training management | master_data |
| 04 | **finance** | Payroll configuration and processing | hr |
| 05 | **scm** | Sales, Purchasing, Inventory, Manufacturing | master_data |
| 06 | **rules** | Business validations for all domains | hr, finance, scm |
| 07 | **services** | API services for all domains | rules |
| 08 | **security** | Access control policies | services |

## Installation Order

```
01_core → 02_master_data → 03_hr → 04_finance ─┐
                        └→ 05_scm ────────────────┴→ 06_rules → 07_services → 08_security
```

## Compilation

```bash
# Compile individual module
dotnet run --project src/BMMDL.Compiler -- pipeline erp_modules/01_core/module.bmmdl

# Verify all modules
for module in erp_modules/*/module.bmmdl; do
    dotnet run --project src/BMMDL.Compiler -- pipeline "$module"
done
```

## Module Declaration Syntax

Each module uses the new BMMDL module syntax:

```cds
module ModuleName version '1.0.0' {
    author: 'Team Name';
    description: 'Module description';
    depends on OtherModule version '>=1.0.0';
    imports OtherNamespace;
    publishes ThisNamespace;
}

namespace ThisNamespace;
// ... entities, types, rules, etc.
```
