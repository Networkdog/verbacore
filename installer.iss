; VerbaCore Installer Script for Inno Setup
; https://jrsoftware.org/isinfo.php

#define MyAppName "VerbaCore"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "Networkdog"
#define MyAppURL "https://github.com/Networkdog/verbacore"
#define MyAppExeName "VerbaCore.exe"

[Setup]
AppId={{B3F8A2E1-7C4D-4E5A-9B2F-1A3C5D7E9F0B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=VerbaCore-Setup-{#MyAppVersion}
OutputDir=installer-output
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 생성"; GroupDescription: "추가 옵션:"; Flags: unchecked
Name: "startup"; Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 옵션:"

[Files]
Source: "publish-standalone\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Icons are embedded as WPF resources inside the exe — no need to ship them separately.

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} 실행"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifdoesntexist runasoriginaluser; Check: IsSilentInstall

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\VerbaCore"

[Code]
function IsSilentInstall: Boolean;
begin
  Result := WizardSilent;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  // Force-kill VerbaCore.exe if still running (fallback for Restart Manager)
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
