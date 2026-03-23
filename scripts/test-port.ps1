# Simple test of port-forward detection logic
Write-Host "Testing PostgreSQL port detection..." -ForegroundColor Cyan

try {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $tcpClient.Connect("localhost", 5432)
    $tcpClient.Close()
    Write-Host "✓ Port 5432 is accessible!" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "✗ Port 5432 is NOT accessible" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
    exit 1
}
