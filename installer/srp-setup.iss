; SRP Smart Chamber Monitoring - Inno Setup script
; Requires Inno Setup 6: https://jrsoftware.org/isinfo.php
; 1) Run build-publish.ps1
; 2) Compile this script (ISCC.exe srp-setup.iss)

#define MyAppName "SRP Smart Chamber Monitoring"
#define MyAppVersion "1.0.0"
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
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"
Name: "startupicon"; Description: "Start SRP automatically when I log on to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Launcher at install root
Source: "publish\SrpLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\app.ico"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; Applications
Source: "publish\DashboardService\*"; DestDir: "{app}\DashboardService"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\RfidService\*"; DestDir: "{app}\RfidService"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM DashboardService.exe /T"; Flags: runhidden; RunOnceId: "KillDashboard"
Filename: "taskkill.exe"; Parameters: "/F /IM RfidService.exe /T"; Flags: runhidden; RunOnceId: "KillRfidService"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  MsgBox('Prerequisite: PostgreSQL must already be installed and running on this PC.' + #13#10 + #13#10 +
         'Default database: smart_monitoring' + #13#10 +
         'After install, the desktop/start icon launches Dashboard and starts the RFID service in the background.',
         mbInformation, MB_OK);
end;
