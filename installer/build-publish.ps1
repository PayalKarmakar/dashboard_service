# Publishes Dashboard, RfidService, SensorService, CameraService, and SrpLauncher
# Expected layout:
#   <workspace>/dashboard_service/installer/   (this script)
#   <workspace>/dashboard_service/DashboardService/
#   <workspace>/rfid_service/
#   <workspace>/sensor_service/

$ErrorActionPreference = "Stop"

$installerRoot = $PSScriptRoot
$dashboardRoot = Split-Path -Parent $installerRoot
$workspaceRoot = Split-Path -Parent $dashboardRoot
$publishRoot = Join-Path $installerRoot "publish"

Write-Host "Workspace:      $workspaceRoot"
Write-Host "Dashboard root: $dashboardRoot"
Write-Host "Installer root: $installerRoot"
Write-Host "Publish root:   $publishRoot"

function Publish-App {
    param(
        [string]$ProjectPath,
        [string]$OutputDir,
        [string]$AssemblyName
    )

    if (-not (Test-Path $ProjectPath)) {
        throw "Project not found: $ProjectPath"
    }

    Write-Host "`n=== Publishing $AssemblyName ===" -ForegroundColor Cyan
    Write-Host "Project: $ProjectPath"
    Write-Host "Output:  $OutputDir"

    if (Test-Path $OutputDir) {
        Remove-Item $OutputDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    & dotnet publish $ProjectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=$false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:AssemblyName=$AssemblyName `
        -o $OutputDir

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $AssemblyName"
    }

    $exe = Join-Path $OutputDir "$AssemblyName.exe"
    if (-not (Test-Path $exe)) {
        throw "Expected EXE not found: $exe"
    }

    Write-Host "OK: $exe" -ForegroundColor Green
}

$dashboardProj = Join-Path $dashboardRoot "DashboardService\DashboardService.csproj"
$rfidServiceProj = Join-Path $workspaceRoot "rfid_service\RfidManagementSystem\RfidManagementSystem.csproj"
$sensorServiceProj = Join-Path $workspaceRoot "sensor_service\SmartMonitoring.SensorService\SmartMonitoring.SensorService\SmartMonitoring.SensorService.csproj"
$cameraServiceSrc = Join-Path $dashboardRoot "camera_service"
$launcherProj = Join-Path $installerRoot "SrpLauncher\SrpLauncher.csproj"

Publish-App -ProjectPath $dashboardProj -OutputDir (Join-Path $publishRoot "DashboardService") -AssemblyName "DashboardService"
Publish-App -ProjectPath $rfidServiceProj -OutputDir (Join-Path $publishRoot "RfidService") -AssemblyName "RfidService"
Publish-App -ProjectPath $sensorServiceProj -OutputDir (Join-Path $publishRoot "SensorService") -AssemblyName "SensorService"

# Camera Python service (source + starter; runtime installed by Setup via Install-CameraRuntime.ps1)
Write-Host "`n=== Packaging CameraService ===" -ForegroundColor Cyan
$cameraOut = Join-Path $publishRoot "CameraService"
if (Test-Path $cameraOut) { Remove-Item $cameraOut -Recurse -Force }
New-Item -ItemType Directory -Force -Path $cameraOut | Out-Null
if (-not (Test-Path $cameraServiceSrc)) {
    throw "Camera service folder not found: $cameraServiceSrc"
}
Copy-Item (Join-Path $cameraServiceSrc "*") $cameraOut -Recurse -Force
Copy-Item (Join-Path $installerRoot "camera-start-camera-service.cmd") (Join-Path $cameraOut "start-camera-service.cmd") -Force
Copy-Item (Join-Path $installerRoot "Install-CameraRuntime.ps1") (Join-Path $publishRoot "Install-CameraRuntime.ps1") -Force
# Drop local venv from package if present (too large / machine-specific)
$venvPath = Join-Path $cameraOut ".venv"
if (Test-Path $venvPath) { Remove-Item $venvPath -Recurse -Force }
Write-Host "OK: $cameraOut" -ForegroundColor Green

# Bundle official Python installer for offline-ish first-time setup
Write-Host "`n=== Packaging Python installer (Prereqs) ===" -ForegroundColor Cyan
$prereqDir = Join-Path $publishRoot "Prereqs"
New-Item -ItemType Directory -Force -Path $prereqDir | Out-Null
$pythonInstaller = Join-Path $prereqDir "python-installer.exe"
$pythonUrl = "https://www.python.org/ftp/python/3.12.8/python-3.12.8-amd64.exe"
if (-not (Test-Path $pythonInstaller) -or (Get-Item $pythonInstaller).Length -lt 1MB) {
    Write-Host "Downloading Python 3.12.8 installer..."
    Invoke-WebRequest -Uri $pythonUrl -OutFile $pythonInstaller -UseBasicParsing
}
Write-Host "OK: $pythonInstaller ($([math]::Round((Get-Item $pythonInstaller).Length/1MB,1)) MB)" -ForegroundColor Green

# Drop stale publish leftovers
$legacyRfidMgmt = Join-Path $publishRoot "RfidManagement"
if (Test-Path $legacyRfidMgmt) {
    Remove-Item $legacyRfidMgmt -Recurse -Force
}

Write-Host "`n=== Publishing SrpLauncher (single-file) ===" -ForegroundColor Cyan
$launcherOut = Join-Path $publishRoot "_launcher_build"
if (Test-Path $launcherOut) { Remove-Item $launcherOut -Recurse -Force }
New-Item -ItemType Directory -Force -Path $launcherOut | Out-Null

& dotnet publish $launcherProj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:AssemblyName=SrpLauncher `
    -o $launcherOut

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed for SrpLauncher"
}

$launcherExe = Join-Path $launcherOut "SrpLauncher.exe"
if (-not (Test-Path $launcherExe)) {
    throw "Expected EXE not found: $launcherExe"
}

Copy-Item $launcherExe (Join-Path $publishRoot "SrpLauncher.exe") -Force
Copy-Item (Join-Path $installerRoot "SrpLauncher\app.ico") (Join-Path $publishRoot "app.ico") -Force -ErrorAction SilentlyContinue
Remove-Item $launcherOut -Recurse -Force
Write-Host "OK: $(Join-Path $publishRoot 'SrpLauncher.exe')" -ForegroundColor Green

Write-Host "`n=== Publish complete ===" -ForegroundColor Green
Write-Host "Next: compile srp-setup.iss with Inno Setup to create Setup.exe"
Write-Host "Publish folder: $publishRoot"
Get-ChildItem $publishRoot | Format-Table Name, Mode, Length
