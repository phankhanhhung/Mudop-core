# BMMDL Port Forward Script with Auto-Reconnect
# Run this in a dedicated terminal window

param([switch]$Postgres, [switch]$PgAdmin, [switch]$All)

if (-not $Postgres -and -not $PgAdmin -and -not $All) { $All = $true }

Write-Host "=== BMMDL Port Forwarding ===" -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop" -ForegroundColor Yellow

$jobs = @()

if ($Postgres -or $All) {
    Write-Host "Starting PostgreSQL port-forward (5432)..." -ForegroundColor Green
    $jobs += Start-Job -Name "postgres" -ScriptBlock {
        while ($true) {
            kubectl port-forward svc/postgres 5432:5432 -n bmmdl 2>&1
            Write-Host "Postgres disconnected, reconnecting in 2s..."
            Start-Sleep -Seconds 2
        }
    }
}

if ($PgAdmin -or $All) {
    Write-Host "Starting pgAdmin port-forward (8080)..." -ForegroundColor Green
    $jobs += Start-Job -Name "pgadmin" -ScriptBlock {
        while ($true) {
            kubectl port-forward svc/pgadmin 8080:80 -n bmmdl 2>&1
            Write-Host "pgAdmin disconnected, reconnecting in 2s..."
            Start-Sleep -Seconds 2
        }
    }
}

Write-Host "`nActive forwards:" -ForegroundColor Cyan
Write-Host "  PostgreSQL: localhost:5432"
Write-Host "  pgAdmin:    http://localhost:8080"

try {
    while ($true) {
        $jobs | Receive-Job -Keep | Out-Null
        Start-Sleep -Seconds 5
    }
}
finally {
    $jobs | Stop-Job | Remove-Job
    Write-Host "`nPort forwards stopped." -ForegroundColor Yellow
}
