@echo off
set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
cd /d "%~dp0"
"%DOTNET_ROOT%\dotnet.exe" run --project "DashboardService\DashboardService.csproj" -c Debug
