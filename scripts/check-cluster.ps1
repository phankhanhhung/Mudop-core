# BMMDL Cluster Health Check Script
# Run in a separate terminal

Write-Host "=== BMMDL Cluster Health Check ===" -ForegroundColor Cyan

# Check pods
Write-Host "`n[1/4] Checking pods..." -ForegroundColor Yellow
kubectl get pods -n bmmdl -o wide

# Check services
Write-Host "`n[2/4] Checking services..." -ForegroundColor Yellow
kubectl get svc -n bmmdl

# Check Postgres connectivity
Write-Host "`n[3/4] Checking PostgreSQL..." -ForegroundColor Yellow
kubectl exec deploy/postgres -n bmmdl -- psql -U bmmdl -d postgres -c "SELECT version();" 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "PostgreSQL: OK" -ForegroundColor Green
} else {
    Write-Host "PostgreSQL: FAILED" -ForegroundColor Red
}

# Check databases
Write-Host "`n[4/4] Listing databases..." -ForegroundColor Yellow
kubectl exec deploy/postgres -n bmmdl -- psql -U bmmdl -d postgres -c "\l"

Write-Host "`n=== Health Check Complete ===" -ForegroundColor Cyan
Write-Host "`nTip: Start port-forwarding with:"
Write-Host "  kubectl port-forward svc/postgres 5432:5432 -n bmmdl"
Write-Host "  kubectl port-forward svc/pgadmin 8080:80 -n bmmdl"
