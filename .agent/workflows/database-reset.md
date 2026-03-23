---
description: Reset và setup lại database PostgreSQL cho development
---

# Database Reset

Reset database PostgreSQL về trạng thái ban đầu.

## Quick Reset

### Reset sử dụng script có sẵn
// turbo
```powershell
scripts/reset_db.ps1
```

### Clear database
// turbo
```powershell
scripts/clear-db.ps1
```

## Manual Reset Steps

### 1. Start PostgreSQL container (nếu chưa chạy)
// turbo
```powershell
docker compose -f infra/docker/docker-compose.unit-test.yml up -d
```

### 2. Chạy database_setup.sql
```powershell
# Sử dụng psql trong container
docker exec -i bmmdl-postgres-unit psql -U bmmdl -d bmmdl_registry < database_setup.sql
```

Hoặc:
```powershell
# Nếu có psql local
$env:PGPASSWORD = "bmmdl123"
psql -h localhost -p 5433 -U bmmdl -d bmmdl_registry -f database_setup.sql
```

### 3. Verify database
// turbo
```powershell
# Check tables exist
docker exec bmmdl-postgres-unit psql -U bmmdl -d bmmdl_registry -c "\dt"
```

## Reset Specific Tables

### Clear all business data (giữ schema)
```sql
TRUNCATE TABLE 
    expression_nodes,
    entity_fields, 
    entity_associations,
    entity_compositions,
    entities,
    services,
    views,
    rules,
    access_controls,
    modules
CASCADE;
```

### Clear modules only
```sql
DELETE FROM modules;
```

## Connection Info

| Environment | Host | Port | Database | User | Password |
|-------------|------|------|----------|------|----------|
| Unit Test | localhost | 5433 | bmmdl_registry | bmmdl | bmmdl123 |
| Development | localhost | 5432 | bmmdl | postgres | postgres |
| K8s | postgres.bmmdl | 5432 | bmmdl | postgres | - |

## Troubleshooting

### Container không start được
```powershell
# Check logs
docker logs bmmdl-postgres-unit

# Remove và recreate
docker compose -f infra/docker/docker-compose.unit-test.yml down -v
docker compose -f infra/docker/docker-compose.unit-test.yml up -d
```

### Permission errors
```sql
-- Grant all permissions to bmmdl user
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO bmmdl;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO bmmdl;
```
