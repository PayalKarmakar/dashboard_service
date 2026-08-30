# SRP Windows Installer

Builds a single **SRP-Setup.exe** that installs:

| App | Role |
|---|---|
| **RfidService** | Background (RFID TCP + API) |
| **DashboardService** | Foreground UI |
| **SrpLauncher** | Desktop icon — starts RfidService then Dashboard |

## Prerequisites (build machine)

1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. [Inno Setup 6](https://jrsoftware.org/isinfo.php) (includes `ISCC.exe`)

## Build steps

```powershell
cd D:\freelan\installer
.\build-publish.ps1

# Compile installer (Inno Setup 6):
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" .\srp-setup.iss
# or: & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\srp-setup.iss
```

Output: `D:\freelan\installer\output\SRP-Setup.exe`

## Install / run (end user)

1. Run `SRP-Setup.exe` (admin)
2. PostgreSQL must already be installed
3. Optionally check **Create a desktop icon**
4. Shortcut runs `SrpLauncher.exe` → backends with `--background` + Dashboard

## Uninstall

Add/Remove Programs → SRP Smart Chamber Monitoring (stops processes then removes files)
