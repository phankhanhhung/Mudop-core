#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run Unit Tests with Docker PostgreSQL.

.DESCRIPTION
    Starts PostgreSQL container (port 5433) and runs dotnet test directly on workstation.
    Output goes to console for easy debugging.

.PARAMETER Category
    Test category: All, Compiler, CodeGen, MetaModel
    Default: All

.PARAMETER Verbose
    Enable detailed test output

.PARAMETER SkipDocker
    Skip Docker container management (assume already running)

.EXAMPLE
    .\run-unit-tests.ps1
    
.EXAMPLE
    .\run-unit-tests.ps1 -Category Compiler -Verbose
#>

param(
    [ValidateSet('All', 'Compiler', 'CodeGen', 'MetaModel', 'Plugins')]
    [string]$Category = 'All',
    [switch]$Verbose,
    [switch]$SkipDocker
)

$ErrorActionPreference = "Continue"
$composeFile = "infra/docker/docker-compose.unit-test.yml"
$testProject = "src/BMMDL.Tests/BMMDL.Tests.csproj"

# Colors
function Write-Header($msg) { 
    Write-Host ""
    Write-Host ("=" * 50) -ForegroundColor Magenta
    Write-Host "  $msg" -ForegroundColor Magenta
    Write-Host ("=" * 50) -ForegroundColor Magenta
    Write-Host ""
}
function Write-Ok($msg) { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Err($msg) { Write-Host "  [ERR] $msg" -ForegroundColor Red }
function Write-Info($msg) { Write-Host "  $msg" -ForegroundColor Cyan }

Write-Header "BMMDL UNIT TESTS (Docker)"
Write-Info "Category: $Category"
Write-Info "Verbose:  $Verbose"

# Category filters (exclude Integration tests)
$filters = @{
    'All'       = 'Category!=Integration'
    'Compiler'  = 'FullyQualifiedName~BMMDL.Tests.Compiler&Category!=Integration'
    'CodeGen'   = 'FullyQualifiedName~BMMDL.Tests.CodeGen&Category!=Integration'
    'MetaModel' = 'FullyQualifiedName~BMMDL.Tests.MetaModel&Category!=Integration'
    'Plugins'   = 'FullyQualifiedName~BMMDL.Tests.Plugins&Category!=Integration'
}

# Start Docker container if needed
if (-not $SkipDocker) {
    Write-Host "`nStarting PostgreSQL container..." -ForegroundColor Yellow
    
    # Check if container exists and is running
    $containerStatus = docker ps -a --filter "name=bmmdl-postgres-unit" --format "{{.Status}}" 2>$null
    
    if ($containerStatus -match "Up") {
        Write-Ok "Container already running"
    }
    else {
        docker compose -f $composeFile up -d 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Err "Failed to start Docker container"
            exit 1
        }
        
        # Wait for healthy
        Write-Info "Waiting for PostgreSQL to be ready..."
        $maxWait = 30
        $waited = 0
        while ($waited -lt $maxWait) {
            $health = docker inspect --format='{{.State.Health.Status}}' bmmdl-postgres-unit 2>$null
            if ($health -eq "healthy") {
                Write-Ok "PostgreSQL ready on port 5433"
                break
            }
            Start-Sleep -Seconds 1
            $waited++
        }
        
        if ($waited -ge $maxWait) {
            Write-Err "PostgreSQL failed to start within $maxWait seconds"
            exit 1
        }
    }
}

# Build test arguments
$testArgs = @("test", $testProject, "--filter", $filters[$Category])

# Output settings per user rules: use trx logger to artifacts/
$artifactsDir = "artifacts"
if (-not (Test-Path $artifactsDir)) { New-Item -ItemType Directory -Path $artifactsDir | Out-Null }
$testArgs += "--logger"
$testArgs += "trx;LogFileName=unit_test_results.trx"
$testArgs += "--results-directory"
$testArgs += $artifactsDir

if ($Verbose) {
    $testArgs += "--logger"
    $testArgs += "console;verbosity=detailed"
}
else {
    $testArgs += "--logger"
    $testArgs += "console;verbosity=normal"
}

# Set connection string for tests (port 5433)
$env:ConnectionStrings__Default = "Host=localhost;Port=5433;Database=bmmdl_registry;Username=bmmdl;Password=bmmdl123"

Write-Host "`nRunning tests..." -ForegroundColor Yellow
Write-Host "Command: dotnet $($testArgs -join ' ')" -ForegroundColor Gray
Write-Host ""

# Run tests
& dotnet @testArgs
$exitCode = $LASTEXITCODE

# Summary
Write-Header "TEST COMPLETE"
if ($exitCode -eq 0) {
    Write-Ok "ALL TESTS PASSED"
    Write-Info "Results: $artifactsDir/unit_test_results.trx"
}
else {
    Write-Err "SOME TESTS FAILED"
    Write-Info "Results: $artifactsDir/unit_test_results.trx"
}

exit $exitCode
