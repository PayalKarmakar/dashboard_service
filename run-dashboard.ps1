# SRP Smart Chamber Monitoring — WPF launcher
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectRoot "DashboardService\DashboardService.csproj"

if (-not (Test-Path $dotnet)) {
    Write-Host "ERROR: .NET 8 SDK not found at $dotnet" -ForegroundColor Red
    Write-Host "Install: https://dotnet.microsoft.com/download/dotnet/8.0"
    Read-Host "Press Enter to exit"
    exit 1
}

Set-Location $projectRoot
Write-Host "Building..." -ForegroundColor Cyan
& $dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Starting dashboard (login window should open)..." -ForegroundColor Green
& $dotnet run --project $project -c Debug --no-build
