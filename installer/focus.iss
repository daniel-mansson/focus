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
Name: "{group}\Focus"; Filename: "{app}\{#MyAppExeName}"; Parameters: "daemon --background"; IconFilename: "{app}\{#MyAppExeName}"

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""FocusDaemon"" /F"; Flags: runhidden; RunOnceId: "DeleteFocusDaemonTask"

[Code]

var
  StartupPage: TInputOptionWizardPage;

// ---------------------------------------------------------------------------
// BuildTaskXml: Build the Task Scheduler XML for importing via schtasks /XML
// ---------------------------------------------------------------------------
function BuildTaskXml(AppPath: String; RunElevated: Boolean): String;
var
  RunLevel: String;
begin
  if RunElevated then
    RunLevel := 'HighestAvailable'
  else
    RunLevel := 'LeastPrivilege';

  Result :=
    '<?xml version="1.0" encoding="UTF-16"?>' + #13#10 +
    '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' + #13#10 +
    '  <RegistrationInfo>' + #13#10 +
    '    <Description>Focus daemon - window navigation</Description>' + #13#10 +
    '  </RegistrationInfo>' + #13#10 +
    '  <Triggers>' + #13#10 +
    '    <LogonTrigger>' + #13#10 +
    '      <Enabled>true</Enabled>' + #13#10 +
    '    </LogonTrigger>' + #13#10 +
    '  </Triggers>' + #13#10 +
    '  <Principals>' + #13#10 +
    '    <Principal id="Author">' + #13#10 +
    '      <LogonType>InteractiveToken</LogonType>' + #13#10 +
    '      <RunLevel>' + RunLevel + '</RunLevel>' + #13#10 +
    '    </Principal>' + #13#10 +
    '  </Principals>' + #13#10 +
    '  <Settings>' + #13#10 +
    '    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' + #13#10 +
    '    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' + #13#10 +
    '    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' + #13#10 +
    '    <AllowHardTerminate>true</AllowHardTerminate>' + #13#10 +
    '    <StartWhenAvailable>false</StartWhenAvailable>' + #13#10 +
    '    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>' + #13#10 +
    '    <AllowStartOnDemand>true</AllowStartOnDemand>' + #13#10 +
    '    <Enabled>true</Enabled>' + #13#10 +
    '    <Hidden>false</Hidden>' + #13#10 +
    '    <RunOnlyIfIdle>false</RunOnlyIfIdle>' + #13#10 +
    '    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>' + #13#10 +
    '    <Priority>7</Priority>' + #13#10 +
    '  </Settings>' + #13#10 +
    '  <Actions Context="Author">' + #13#10 +
    '    <Exec>' + #13#10 +
    '      <Command>' + AppPath + '</Command>' + #13#10 +
    '      <Arguments>daemon --background</Arguments>' + #13#10 +
    '    </Exec>' + #13#10 +
    '  </Actions>' + #13#10 +
    '</Task>';
end;

// ---------------------------------------------------------------------------
// DetectExistingTask: Check if FocusDaemon task exists and detect its RunLevel
// Returns True if task exists; sets IsElevated to True if RunLevel is HighestAvailable
// ---------------------------------------------------------------------------
function DetectExistingTask(var IsElevated: Boolean): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  Output: AnsiString;
begin
  Result := False;
  IsElevated := False;

  // Check if task exists (exit code 0 = exists, non-zero = not found)
  Exec('schtasks.exe', '/Query /TN "FocusDaemon"', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then Exit;

  Result := True;

  // Query XML output to detect RunLevel -- redirect via cmd.exe since Exec cannot capture stdout
  TmpFile := ExpandConstant('{tmp}\FocusDaemonQuery.xml');
  Exec(ExpandConstant('{cmd}'),
    '/C schtasks.exe /Query /TN "FocusDaemon" /XML > "' + TmpFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if LoadStringFromFile(TmpFile, Output) then
    IsElevated := Pos('HighestAvailable', Output) > 0;
end;

// ---------------------------------------------------------------------------
// InitializeWizard: Create the custom Startup Options wizard page
// ---------------------------------------------------------------------------
procedure InitializeWizard();
var
  TaskExists: Boolean;
  IsElevated: Boolean;
begin
  StartupPage := CreateInputOptionPage(wpSelectDir,
    'Startup Options', 'Configure how Focus starts.',
    'Select startup behavior, then click Next.',
    False, False);  // False = checkboxes (not radio), False = no listbox
  StartupPage.Add('Start at logon');
  StartupPage.Add('Run elevated (admin) - Required to navigate between admin windows');

  // Defaults: start at logon = on, run elevated = off
  StartupPage.Values[0] := True;
  StartupPage.Values[1] := False;

  // On upgrade/reinstall: pre-populate from existing task state
  TaskExists := DetectExistingTask(IsElevated);
  if TaskExists then
  begin
    StartupPage.Values[0] := True;
    StartupPage.Values[1] := IsElevated;
  end;
end;

// ---------------------------------------------------------------------------
// CurStepChanged: Create or delete the scheduled task after install completes
// ---------------------------------------------------------------------------
procedure CurStepChanged(CurStep: TSetupStep);
var
  AppPath, XmlPath, Xml, CreateParams: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then Exit;

  if StartupPage.Values[0] then
  begin
    // "Start at logon" is checked -- create the scheduled task

    AppPath := ExpandConstant('{app}\focus.exe');
    XmlPath := ExpandConstant('{tmp}\FocusDaemon.xml');
    Xml := BuildTaskXml(AppPath, StartupPage.Values[1]);

    // Write XML to temp file (ANSI encoding is fine since content is pure ASCII)
    SaveStringToFile(XmlPath, Xml, False);

    // Delete any existing task first (ignore failure)
    Exec('schtasks.exe', '/Delete /TN "FocusDaemon" /F', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Create the scheduled task from XML
    // ONLOGON tasks always require admin, so use ShellExec "runas" unless already elevated
    CreateParams := '/Create /XML "' + XmlPath + '" /TN "FocusDaemon" /F';
    if IsAdminInstallMode() then
    begin
      // Installer already running elevated -- schtasks inherits admin rights
      Exec('schtasks.exe', CreateParams, '',
        SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end
    else
    begin
      // Non-elevated installer -- request elevation just for schtasks via UAC
      ShellExec('runas', 'schtasks.exe', CreateParams, '',
        SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;

    // Launch daemon now via the scheduled task (runs elevated if configured)
    Exec('schtasks.exe', '/Run /TN "FocusDaemon"', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else
  begin
    // "Start at logon" is unchecked -- delete any existing task
    // Try non-elevated first, fall back to runas if needed
    Exec('schtasks.exe', '/Delete /TN "FocusDaemon" /F', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if ResultCode <> 0 then
      ShellExec('runas', 'schtasks.exe', '/Delete /TN "FocusDaemon" /F', '',
        SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

// ---------------------------------------------------------------------------
// CurUninstallStepChanged: Robust cleanup of scheduled task during uninstall
// (supplements the [UninstallRun] entry as a fallback for elevated tasks)
// ---------------------------------------------------------------------------
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usPostUninstall then Exit;

  // Try non-elevated first (works if task was created with LeastPrivilege)
  Exec('schtasks.exe', '/Delete /TN "FocusDaemon" /F', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // If that failed (e.g. elevated task requires admin to delete), try with runas
  if ResultCode <> 0 then
    ShellExec('runas', 'schtasks.exe', '/Delete /TN "FocusDaemon" /F', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
