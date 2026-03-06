; Focus - Inno Setup Script
; Produces Focus-Setup.exe for install, upgrade, and uninstall lifecycle

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "Focus"
#define MyAppExeName "focus.exe"

[Setup]
AppId=Focus
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Daniel
DefaultDirName={localappdata}\Focus
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=output
OutputBaseFilename=Focus-Setup
SetupIconFile=..\focus\focus.ico
UninstallDisplayIcon={app}\focus.exe
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Global\focus-daemon
CloseApplications=yes
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\focus\bin\Release\net8.0-windows\win-x64\publish\focus.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Focus"; Filename: "{app}\{#MyAppExeName}"; Parameters: "daemon"; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "daemon"; Description: "Launch Focus now"; Flags: postinstall nowait skipifsilent
