@echo off
set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not exist "%DOTNET%" (
    echo ERROR: .NET SDK not found at %DOTNET%
    echo Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

cd /d "%~dp0"
echo Building DashboardService...
"%DOTNET%" build "DashboardService\DashboardService.csproj" -c Debug
if errorlevel 1 (
    echo BUILD FAILED - see errors above.
    pause
    exit /b 1
)

echo.
echo Starting SRP Smart Chamber Monitoring...
"%DOTNET%" run --project "DashboardService\DashboardService.csproj" -c Debug --no-build
if errorlevel 1 (
    echo App exited with error.
    pause
    exit /b 1
)
