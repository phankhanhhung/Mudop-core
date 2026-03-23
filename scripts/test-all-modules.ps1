#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Tests CLI publish by sequentially installing all 8 ERP modules.

.DESCRIPTION
    This script installs all 8 ERP modules (01_core through 08_security) sequentially
    into a fresh PostgreSQL database, verifying the complete CLI publish flow.

.PARAMETER TenantId
    The tenant ID to use for publishing. Defaults to a generated GUID.

.PARAMETER ConnectionString
    The database connection string. Defaults to localhost:5432.

.PARAMETER ClearDb
    If specified, clears the database before starting.

.EXAMPLE
    .\test-all-modules.ps1
    
.EXAMPLE
    .\test-all-modules.ps1 -ClearDb
    
.EXAMPLE
    .\test-all-modules.ps1 -TenantId "12345678-1234-1234-1234-123456789012"
#>

param(
    [string]$TenantId = "44444444-4444-4444-4444-444444444444",
    [string]$ConnectionString = "Host=localhost;Port=5432;Database=bmmdl_registry;Username=bmmdl;Password=bmmdl123",
    [switch]$ClearDb
)

# Clear DB if requested
if ($ClearDb) {
    Write-Host "`nClearing database..." -ForegroundColor Yellow
    & "$PSScriptRoot\clear-db.ps1"
    Write-Host "`nWaiting 5 seconds for DB to settle..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
}

# Ensure port-forward is running
Write-Host "`nChecking PostgreSQL port-forward..." -ForegroundColor Cyan
$portForwardJob = $null
$portIsOpen = $false

try {
    # Test if port 5432 is already accessible
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $tcpClient.Connect("localhost", 5432)
    $tcpClient.Close()
    $portIsOpen = $true
    Write-Host "  ✓ Port 5432 already forwarded" -ForegroundColor Green
}
catch {
    Write-Host "  Starting port-forward job..." -ForegroundColor Gray
    
    # Kill any orphaned kubectl port-forward processes
    Get-Process kubectl -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    
    # Start port-forward in background
    $portForwardJob = Start-Job -Name "postgres-module-test-forward" -ScriptBlock {
        kubectl port-forward svc/postgres 5432:5432 -n bmmdl 2>&1 | Out-Null
    }
    
    # Wait up to 5 seconds for port to be ready
    $maxWait = 5
    $waited = 0
    while ($waited -lt $maxWait) {
        Start-Sleep -Milliseconds 500
        $waited += 0.5
        
        try {
            $tcpClient = New-Object System.Net.Sockets.TcpClient
            $tcpClient.Connect("localhost", 5432)
            $tcpClient.Close()
            $portIsOpen = $true
            Write-Host "  ✓ Port-forward established" -ForegroundColor Green
            break
        }
        catch {
            # Keep waiting
        }
    }
    
    if (-not $portIsOpen) {
        Write-Host "  ERROR: Failed to establish port-forward after $maxWait seconds" -ForegroundColor Red
        if ($portForwardJob) {
            Stop-Job $portForwardJob -ErrorAction SilentlyContinue
            Remove-Job $portForwardJob -ErrorAction SilentlyContinue
        }
        exit 1
    }
}


# Define modules
$modules = @(
    @{ Number = 1; Name = "Core"; Path = "erp_modules/01_core/module.bmmdl" }
    @{ Number = 2; Name = "MasterData"; Path = "erp_modules/02_master_data/module.bmmdl" }
    @{ Number = 3; Name = "HR"; Path = "erp_modules/03_hr/module.bmmdl" }
    @{ Number = 4; Name = "Finance"; Path = "erp_modules/04_finance/module.bmmdl" }
    @{ Number = 5; Name = "SCM"; Path = "erp_modules/05_scm/module.bmmdl" }
    @{ Number = 6; Name = "Rules"; Path = "erp_modules/06_rules/module.bmmdl" }
    @{ Number = 7; Name = "Services"; Path = "erp_modules/07_services/module.bmmdl" }
    @{ Number = 8; Name = "Security"; Path = "erp_modules/08_security/module.bmmdl" }
)

Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  8-MODULE SEQUENTIAL INSTALLATION TEST" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "Tenant:     $TenantId" -ForegroundColor Gray
Write-Host "Connection: $ConnectionString" -ForegroundColor Gray
Write-Host ""

$results = @()
$totalStart = Get-Date

foreach ($module in $modules) {
    $num = $module.Number
    $name = $module.Name
    $path = $module.Path
    
    Write-Host ("[{0}/8] Installing {1}..." -f $num, $name) -ForegroundColor Cyan
    
    $start = Get-Date
    $output = dotnet run --project src/BMMDL.Compiler -- pipeline $path --publish --tenant $TenantId --connection $ConnectionString 2>&1
    $duration = (Get-Date) - $start
    
    # Parse results
    $success = $output | Select-String "Published to database"
    $failed = $output | Select-String "Consistency check failed"
    
    if ($success) {
        $entitiesMatch = $output | Select-String "Entities:\s+(\d+)" | Select-Object -Last 1
        $typesMatch = $output | Select-String "Types:\s+(\d+)" | Select-Object -Last 1
        $enumsMatch = $output | Select-String "Enums:\s+(\d+)" | Select-Object -Last 1
        $aspectsMatch = $output | Select-String "Aspects:\s+(\d+)" | Select-Object -Last 1
        
        $entities = if ($entitiesMatch) { $entitiesMatch.Matches.Groups[1].Value } else { "0" }
        $types = if ($typesMatch) { $typesMatch.Matches.Groups[1].Value } else { "0" }
        $enums = if ($enumsMatch) { $enumsMatch.Matches.Groups[1].Value } else { "0" }
        $aspects = if ($aspectsMatch) { $aspectsMatch.Matches.Groups[1].Value } else { "0" }
        
        Write-Host "  [OK] Success" -ForegroundColor Green
        Write-Host ("     Entities: {0} | Types: {1} | Enums: {2} | Aspects: {3}" -f $entities, $types, $enums, $aspects) -ForegroundColor Gray
        
        $results += @{
            Module   = $name
            Status   = "Success"
            Entities = $entities
            Types    = $types
            Enums    = $enums
            Aspects  = $aspects
            Duration = $duration.TotalSeconds
        }
    }
    elseif ($failed) {
        Write-Host "  [FAIL] Conflict detected" -ForegroundColor Red
        $results += @{
            Module   = $name
            Status   = "Conflict"
            Duration = $duration.TotalSeconds
        }
    }
    else {
        Write-Host "  [FAIL] Unknown error" -ForegroundColor Red
        $results += @{
            Module   = $name
            Status   = "Error"
            Duration = $duration.TotalSeconds
        }
    }
    
    Write-Host ""
}

$totalDuration = (Get-Date) - $totalStart

# Summary
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  INSTALLATION SUMMARY" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

$successCount = ($results | Where-Object { $_.Status -eq "Success" }).Count
$conflictCount = ($results | Where-Object { $_.Status -eq "Conflict" }).Count
$errorCount = ($results | Where-Object { $_.Status -eq "Error" }).Count

Write-Host ("Results:      {0} Success, {1} Conflict, {2} Error" -f $successCount, $conflictCount, $errorCount) -ForegroundColor Gray
Write-Host ("Total Time:   {0}s" -f [math]::Round($totalDuration.TotalSeconds, 2)) -ForegroundColor Gray
Write-Host ""

# Details table
Write-Host "Module Details:" -ForegroundColor Yellow
$results | Where-Object { $_.Status -eq "Success" } | ForEach-Object {
    $name = $_.Module.PadRight(12)
    $entities = $_.Entities.PadLeft(2)
    $types = $_.Types.PadLeft(2)
    $enums = $_.Enums.PadLeft(2)
    $aspects = $_.Aspects.PadLeft(2)
    $duration = [math]::Round($_.Duration, 2)
    Write-Host ("  {0}  E:{1} T:{2} En:{3} A:{4}  {5}s" -f $name, $entities, $types, $enums, $aspects, $duration) -ForegroundColor Gray
}

Write-Host ""

# Cleanup port-forward if we started it
if ($portForwardJob) {
    Write-Host "Stopping port-forward job..." -ForegroundColor Gray
    Stop-Job $portForwardJob -ErrorAction SilentlyContinue
    Remove-Job $portForwardJob -ErrorAction SilentlyContinue
    Write-Host "  ✓ Port-forward stopped" -ForegroundColor Green
}

if ($successCount -eq 8) {
    Write-Host "SUCCESS: All 8 modules installed successfully!" -ForegroundColor Green
}
elseif ($conflictCount -gt 0) {
    Write-Host "WARNING: Some modules detected conflicts (already installed)" -ForegroundColor Yellow
}
else {
    Write-Host "ERROR: Some modules failed to install" -ForegroundColor Red
}
