# reset_db.ps1 - Clear all BMMDL data from PostgreSQL (Windows)
# Usage: .\scripts\reset_db.ps1

Write-Host "🗑️  Resetting BMMDL database..." -ForegroundColor Yellow

kubectl exec deploy/postgres -n bmmdl -- psql -U bmmdl -d bmmdl -c @'
TRUNCATE 
    types, enums, aspects, entities, services, views, rules, sequences, events,
    access_controls, modules, module_dependencies, module_installations, namespaces
RESTART IDENTITY CASCADE;
'@

Write-Host "✅ Database reset complete" -ForegroundColor Green
