# Quick health check for SRP install (RFID + Camera + DB-related ports).
$ErrorActionPreference = 'Continue'
$root = 'C:\Program Files\SRP Innovations'

Write-Host "`n=== SRP server diagnostics ===" -ForegroundColor Cyan

function Test-PortListen([int]$Port) {
    $c = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $c) {
        Write-Host "Port $Port : NOT listening" -ForegroundColor Red
        return
    }
    $proc = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
    $name = if ($proc) { $proc.ProcessName } else { '?' }
    Write-Host "Port $Port : LISTENING (PID $($c.OwningProcess) $name)" -ForegroundColor Green
}

Test-PortListen 64312  # RfidService HTTP API
Test-PortListen 64316  # CameraService (uvicorn)

foreach ($p in @('RfidService', 'DashboardService', 'SensorService')) {
    $procs = Get-Process -Name $p -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "$p : running ($($procs.Count) process(es))" -ForegroundColor Green
    } else {
        Write-Host "$p : not running" -ForegroundColor Yellow
    }
}

try {
    $r = Invoke-WebRequest -Uri 'http://127.0.0.1:64312/api/readers/status' -UseBasicParsing -TimeoutSec 5
    Write-Host "RfidService /api/readers/status : HTTP $($r.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "RfidService /api/readers/status : FAILED ($($_.Exception.Message))" -ForegroundColor Red
}

try {
    $c = Invoke-WebRequest -Uri 'http://127.0.0.1:64316/api/health' -UseBasicParsing -TimeoutSec 5
    Write-Host "CameraService /api/health : HTTP $($c.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "CameraService /api/health : FAILED ($($_.Exception.Message))" -ForegroundColor Red
}

Write-Host @"

Notes:
- WinError 10048 on port 64316 = a second CameraService was started while one is already running.
  Do NOT run start-camera-service.cmd manually if SRP Startup/Launcher already started camera.
- RFID entry/exit in rfid_transactions needs RfidService.exe (--background) and readers connected.
- Camera crossing logs need Dashboard OPEN + login (background monitoring starts after login).

Restart backends only:
  & `"$root\SrpLauncher.exe`" --backends-only
"@
