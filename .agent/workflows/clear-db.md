---
description: Xóa toàn bộ schemas và data trong database để reset về trạng thái trống
---

# Clear Database

Xóa toàn bộ business schemas và registry data để reset database về trạng thái ban đầu.

## Quick Clear (Xóa tất cả schemas)

### 1. Xóa platform schema
// turbo
```powershell
docker exec bmmdl-postgres psql -U bmmdl -d bmmdl_registry -c "DROP SCHEMA IF EXISTS platform CASCADE;"
```

### 2. Truncate registry tables
// turbo
```powershell
docker exec bmmdl-postgres psql -U bmmdl -d bmmdl_registry -c "TRUNCATE TABLE registry.expression_nodes, registry.entity_fields, registry.entity_associations, registry.entities, registry.modules, registry.tenants CASCADE;"
```

## Notes

- Sau khi clear, chạy `/platform-bootstrap` để khôi phục
- Business schemas: platform, scm, hr, finance, crm, inventory, workflow, master_data, config
- Registry tables lưu meta-model definitions
