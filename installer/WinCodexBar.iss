#define AppName "Win CodexBar"
#define AppExeName "CodexBar.Windows.exe"
#define AppVersion "1.0.0"
#ifndef SourceDir
#define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{7E3F5B71-3E21-4F27-8C7F-CCDF69C0C7BD}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Win CodexBar
DefaultDirName={localappdata}\Programs\Win CodexBar
DefaultGroupName=Win CodexBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutputDir}
OutputBaseFilename=WinCodexBarSetup
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
SetupLogging=yes

[Tasks]
Name: "startup"; Description: "Start Win CodexBar when I sign in"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Win CodexBar"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{userstartup}\Win CodexBar"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: startup
Name: "{userdesktop}\Win CodexBar"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Win CodexBar"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExeName} /F"; Flags: runhidden; RunOnceId: "StopWinCodexBar"
