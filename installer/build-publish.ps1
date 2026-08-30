# Publishes Dashboard, RfidService, and SrpLauncher into installer\publish
$ErrorActionPreference = "Stop"

$installerRoot = $PSScriptRoot
$root = Split-Path -Parent $installerRoot
$publishRoot = Join-Path $installerRoot "publish"

Write-Host "Root:           $root"
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
        -p:PublishSingleFile=false `
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

$dashboardProj = Join-Path $root "dashboard_service\DashboardService\DashboardService.csproj"
$rfidServiceProj = Join-Path $root "rfid_service\RfidManagementSystem\RfidManagementSystem.csproj"
$launcherProj = Join-Path $installerRoot "SrpLauncher\SrpLauncher.csproj"

Publish-App -ProjectPath $dashboardProj -OutputDir (Join-Path $publishRoot "DashboardService") -AssemblyName "DashboardService"
Publish-App -ProjectPath $rfidServiceProj -OutputDir (Join-Path $publishRoot "RfidService") -AssemblyName "RfidService"

# Drop stale RfidManagement publish output if present from older builds
$legacyRfidMgmt = Join-Path $publishRoot "RfidManagement"
if (Test-Path $legacyRfidMgmt) {
    Remove-Item $legacyRfidMgmt -Recurse -Force
}

# Launcher must be a single self-contained EXE (only the .exe is installed at {app} root).
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
