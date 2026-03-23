#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Clears the PostgreSQL database by deleting and recreating the PVC in Kubernetes.

.DESCRIPTION
    This script scales down the postgres deployment, deletes the PVC to wipe data,
    then recreates everything from the k8s manifests.

.EXAMPLE
    .\clear-db.ps1
#>

Write-Host "`n🗑️  Clearing PostgreSQL Database..." -ForegroundColor Yellow

# Scale down postgres
Write-Host "`n[1/5] Scaling down postgres deployment..." -ForegroundColor Cyan
kubectl scale deployment postgres -n bmmdl --replicas=0

Start-Sleep -Seconds 5

# Delete PVC (this wipes the data)
Write-Host "`n[2/5] Deleting PVC (wiping data)..." -ForegroundColor Cyan
kubectl delete pvc postgres-pvc -n bmmdl --force --grace-period=0

Start-Sleep -Seconds 10

# Recreate from manifests
Write-Host "`n[3/5] Applying postgres manifests..." -ForegroundColor Cyan
kubectl apply -f infra/k8s/postgres.yaml

# Wait for deployment
Write-Host "`n[4/5] Waiting for postgres to be ready..." -ForegroundColor Cyan
kubectl wait --for=condition=available deployment/postgres -n bmmdl --timeout=120s

# Kill any existing port-forward processes
Write-Host "`n[5/5] Cleaning up port-forward processes..." -ForegroundColor Cyan
Get-Process kubectl -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "`nDatabase cleared successfully!" -ForegroundColor Green
Write-Host "Fresh PostgreSQL is ready at localhost:5432 (via port-forward)" -ForegroundColor Gray
Write-Host "   Run: .\scripts\port-forward.ps1" -ForegroundColor Gray
