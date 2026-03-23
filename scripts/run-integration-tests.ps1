#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run Integration Tests on Kubernetes.

.DESCRIPTION
    Deploys full test environment to K8s (PostgreSQL + Registry API + Test Runner)
    and runs integration tests. Everything runs inside the cluster.

.PARAMETER KeepNamespace
    Don't delete namespace after tests (for debugging)

.PARAMETER Timeout
    Timeout in seconds to wait for tests. Default: 600 (10 minutes)

.EXAMPLE
    .\run-integration-tests.ps1
    
.EXAMPLE
    .\run-integration-tests.ps1 -KeepNamespace
#>

param(
    [switch]$KeepNamespace,
    [int]$Timeout = 600
)

$ErrorActionPreference = "Stop"
$namespace = "bmmdl-test"
$k8sDir = "infra/k8s/integration-test"
$artifactsDir = "artifacts"

# Colors
function Write-Header($msg) { Write-Host "`n$("=" * 50)" -ForegroundColor Magenta; Write-Host "  $msg" -ForegroundColor Magenta; Write-Host "$("=" * 50)`n" -ForegroundColor Magenta }
function Write-Ok($msg) { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Err($msg) { Write-Host "  ✗ $msg" -ForegroundColor Red }
function Write-Info($msg) { Write-Host "  $msg" -ForegroundColor Cyan }

Write-Header "BMMDL INTEGRATION TESTS (Kubernetes)"
Write-Info "Namespace: $namespace"
Write-Info "Timeout:   ${Timeout}s"

# Ensure artifacts dir exists
if (-not (Test-Path $artifactsDir)) { New-Item -ItemType Directory -Path $artifactsDir | Out-Null }

# Cleanup any existing namespace
Write-Host "`nCleaning up previous test environment..." -ForegroundColor Yellow
kubectl delete namespace $namespace --ignore-not-found=true 2>$null | Out-Null
Start-Sleep -Seconds 2

# Deploy namespace
Write-Host "`nCreating namespace..." -ForegroundColor Yellow
kubectl apply -f "$k8sDir/namespace.yaml"
if ($LASTEXITCODE -ne 0) { Write-Err "Failed to create namespace"; exit 1 }
Write-Ok "Namespace created"

# Deploy PostgreSQL
Write-Host "`nDeploying PostgreSQL..." -ForegroundColor Yellow
kubectl apply -f "$k8sDir/postgres.yaml"
if ($LASTEXITCODE -ne 0) { Write-Err "Failed to deploy PostgreSQL"; exit 1 }

# Wait for PostgreSQL
kubectl wait --for=condition=ready pod -l app=postgres -n $namespace --timeout=120s
if ($LASTEXITCODE -ne 0) { Write-Err "PostgreSQL failed to start"; exit 1 }
Write-Ok "PostgreSQL running"

# Deploy Registry API
Write-Host "`nDeploying Registry API..." -ForegroundColor Yellow
kubectl apply -f "$k8sDir/registry-api.yaml"
if ($LASTEXITCODE -ne 0) { Write-Err "Failed to deploy Registry API"; exit 1 }

# Wait for Registry API
kubectl wait --for=condition=ready pod -l app=registry-api -n $namespace --timeout=120s
if ($LASTEXITCODE -ne 0) { 
    Write-Err "Registry API failed to start"
    Write-Info "Check logs: kubectl logs -l app=registry-api -n $namespace"
    if (-not $KeepNamespace) { kubectl delete namespace $namespace 2>$null }
    exit 1 
}
Write-Ok "Registry API running"

# Deploy Test Runner Job
Write-Host "`nStarting test runner..." -ForegroundColor Yellow
kubectl apply -f "$k8sDir/test-runner.yaml"
if ($LASTEXITCODE -ne 0) { Write-Err "Failed to start test runner"; exit 1 }
Write-Ok "Test runner job created"

# Wait and stream logs
Write-Host "`nWaiting for tests to complete (max ${Timeout}s)..." -ForegroundColor Yellow
Write-Host "-" * 50 -ForegroundColor Gray

# Wait for pod to be created
Start-Sleep -Seconds 5
$podName = kubectl get pods -n $namespace -l app=test-runner -o jsonpath='{.items[0].metadata.name}' 2>$null

if ($podName) {
    # Stream logs
    kubectl logs -f job/integration-tests -n $namespace --all-containers 2>&1
}

# Check job status
$jobStatus = kubectl get job integration-tests -n $namespace -o jsonpath='{.status.succeeded}' 2>$null
$jobFailed = kubectl get job integration-tests -n $namespace -o jsonpath='{.status.failed}' 2>$null

Write-Host "-" * 50 -ForegroundColor Gray

# Copy results if possible
Write-Host "`nCollecting results..." -ForegroundColor Yellow
$resultPod = kubectl get pods -n $namespace -l app=test-runner -o jsonpath='{.items[0].metadata.name}' 2>$null
if ($resultPod) {
    kubectl cp "${namespace}/${resultPod}:/results/integration_test_results.trx" "$artifactsDir/integration_test_results.trx" 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Ok "Results copied to $artifactsDir/integration_test_results.trx"
    }
}

# Cleanup
if (-not $KeepNamespace) {
    Write-Host "`nCleaning up..." -ForegroundColor Yellow
    kubectl delete namespace $namespace 2>$null | Out-Null
    Write-Ok "Namespace deleted"
}
else {
    Write-Info "Namespace kept for debugging: kubectl get all -n $namespace"
}

# Summary
Write-Header "TEST COMPLETE"
if ($jobStatus -eq "1") {
    Write-Ok "ALL INTEGRATION TESTS PASSED"
    exit 0
}
else {
    Write-Err "INTEGRATION TESTS FAILED"
    Write-Info "Results: $artifactsDir/integration_test_results.trx"
    exit 1
}
