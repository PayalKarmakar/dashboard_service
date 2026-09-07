; SRP Smart Chamber Monitoring - Inno Setup script
; Requires Inno Setup 6: https://jrsoftware.org/isinfo.php
; 1) Run build-publish.ps1
; 2) Compile this script (ISCC.exe srp-setup.iss)

#define MyAppName "SRP Smart Chamber Monitoring"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "SRP Innovations"
#define MyAppURL "https://codeinq.com/"
#define MyAppExeName "SrpLauncher.exe"

[Setup]
AppId={{A7C3E2F1-9B84-4D2A-8E61-SRP-CHAMBER-001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\SRP Innovations
DefaultGroupName=SRP Innovations
DisableProgramGroupPage=yes
LicenseFile=
InfoBeforeFile=
OutputDir=output
OutputBaseFilename=SRP-Setup
SetupIconFile=SrpLauncher\app.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon for Dashboard"; GroupDescription: "Additional icons:"; Flags: checkedonce
Name: "startupbackends"; Description: "Start background services (RFID, Sensor, Camera) when I log on — Dashboard stays closed"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
; Launcher at install root
Source: "publish\SrpLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\app.ico"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "publish\Install-CameraRuntime.ps1"; DestDir: "{app}"; Flags: ignoreversion

; Applications
Source: "publish\DashboardService\*"; DestDir: "{app}\DashboardService"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\RfidService\*"; DestDir: "{app}\RfidService"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\SensorService\*"; DestDir: "{app}\SensorService"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\CameraService\*"; DestDir: "{app}\CameraService"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\Prereqs\*"; DestDir: "{app}\Prereqs"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Comment: "Open Dashboard (starts backends if needed)"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Desktop = Dashboard (launcher ensures backends, then opens UI)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon; Comment: "Open Dashboard"
; Startup = backends only (no Dashboard window)
Name: "{commonstartup}\SRP Background Services"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--backends-only"; IconFilename: "{app}\app.ico"; Tasks: startupbackends

[Run]
; 1) Install Python (if needed) + CameraService .venv + pip packages (needs internet for pip)
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-CameraRuntime.ps1"" -CameraServiceDir ""{app}\CameraService"" -PythonInstaller ""{app}\Prereqs\python-installer.exe"""; \
  StatusMsg: "Installing Python and Live Camera packages (may take several minutes)..."; \
  Flags: runhidden waituntilterminated

; 2) Start background services
Filename: "{app}\{#MyAppExeName}"; Parameters: "--backends-only"; Description: "Start background services now"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Description: "Open Dashboard"; Flags: nowait postinstall skipifsilent unchecked

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM DashboardService.exe /T"; Flags: runhidden; RunOnceId: "KillDashboard"
Filename: "taskkill.exe"; Parameters: "/F /IM RfidService.exe /T"; Flags: runhidden; RunOnceId: "KillRfidService"
Filename: "taskkill.exe"; Parameters: "/F /IM SensorService.exe /T"; Flags: runhidden; RunOnceId: "KillSensorService"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  MsgBox('Prerequisites:' + #13#10 +
         '1) PostgreSQL installed (database: smart_monitoring)' + #13#10 +
         '2) Internet connection for first-time Live Camera package install (pip)' + #13#10 + #13#10 +
         'Setup will install Python (if missing) and CameraService dependencies automatically.' + #13#10 + #13#10 +
         'After install:' + #13#10 +
         '- Desktop icon opens Dashboard' + #13#10 +
         '- Windows Startup runs RFID / Sensor / Camera in background only',
         mbInformation, MB_OK);
end;
