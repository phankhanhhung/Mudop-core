#!/bin/bash
# reset_db.sh - Clear all BMMDL data from PostgreSQL
# Usage: ./scripts/reset_db.sh

echo "🗑️  Resetting BMMDL database..."

kubectl exec deploy/postgres -n bmmdl -- psql -U bmmdl -d bmmdl -c '
TRUNCATE 
    types, enums, aspects, entities, services, views, rules, sequences, events,
    access_controls, modules, module_dependencies, module_installations, namespaces
RESTART IDENTITY CASCADE;
'

echo "✅ Database reset complete"
