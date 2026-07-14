#define AppName "WindexBar"
#define AppExeName "WindexBar.Windows.exe"
#ifndef AppVersion
#define AppVersion "1.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif
#ifndef SetupIconFile
#define SetupIconFile "..\src\WindexBar.Windows\Assets\TrayIcon.ico"
#endif

[Setup]
AppId={{7E3F5B71-3E21-4F27-8C7F-CCDF69C0C7BD}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=WindexBar
AppPublisherURL=https://github.com/myagmb28Dev/WindexBar
AppSupportURL=https://github.com/myagmb28Dev/WindexBar/issues
AppUpdatesURL=https://github.com/myagmb28Dev/WindexBar/releases
DefaultDirName={localappdata}\Programs\WindexBar
DefaultGroupName=WindexBar
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=WindexBarSetup
SetupIconFile={#SetupIconFile}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
SetupLogging=yes

[Tasks]
Name: "startup"; Description: "Start WindexBar when I sign in"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{localappdata}\Programs\WindexBar"
Type: filesandordirs; Name: "{userprograms}\WindexBar"
Type: filesandordirs; Name: "{userstartup}\WindexBar.lnk"
Type: filesandordirs; Name: "{userdesktop}\WindexBar.lnk"

[Icons]
Name: "{group}\WindexBar"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{userstartup}\WindexBar"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: startup
Name: "{userdesktop}\WindexBar"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch WindexBar"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExeName} /F"; Flags: runhidden; RunOnceId: "StopWindexBar"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
