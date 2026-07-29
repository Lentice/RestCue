; RestCue installer script for Inno Setup 7
; Build from packaging/windows/build-package.ps1

#define MyAppName "RestCue"
#define MyAppPublisher "Lentice"
#define MyAppURL "https://github.com/Lentice/RestCue"
#define MyAppExeName "RestCue.exe"

; Version auto-detected from built binary — see build-package.ps1
; Override with /dMyAppVersion=x.y.z on ISCC command line
#ifndef MyAppVersion
  #define MyAppVersion "1.3.0"
#endif

[Setup]
AppId=RestCue
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\..\artifacts
OutputBaseFilename=RestCue-{#MyAppVersion}-win-x64
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"

[Files]
Source: "..\..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; Startup is owned by the app setting. Do not create it during install, but
; remove an orphaned entry during uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "RestCue"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
; Only remove app directory — user data in %LocalAppData%\RestCue is preserved
Type: filesandordirs; Name: "{app}"

[InstallDelete]
; Clean up any previous version files not in the new package
Type: filesandordirs; Name: "{app}\*.old"

[Code]
function NextVersionPart(var Version: String): Integer;
var
  Separator: Integer;
  Part: String;
begin
  Separator := Pos('.', Version);
  if Separator = 0 then
  begin
    Part := Version;
    Version := '';
  end
  else
  begin
    Part := Copy(Version, 1, Separator - 1);
    Delete(Version, 1, Separator);
  end;

  Result := StrToIntDef(Part, 0);
end;

function CompareVersions(LeftVersion, RightVersion: String): Integer;
var
  Index: Integer;
  LeftPart: Integer;
  RightPart: Integer;
begin
  Result := 0;
  for Index := 1 to 4 do
  begin
    LeftPart := NextVersionPart(LeftVersion);
    RightPart := NextVersionPart(RightVersion);
    if LeftPart > RightPart then
    begin
      Result := 1;
      Exit;
    end;
    if LeftPart < RightPart then
    begin
      Result := -1;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
begin
  Result := True;
  if RegQueryStringValue(
    HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\RestCue_is1',
    'DisplayVersion',
    InstalledVersion) and
    (CompareVersions(InstalledVersion, '{#MyAppVersion}') > 0) then
  begin
    SuppressibleMsgBox(
      'A newer version of RestCue is already installed. Downgrade is not supported.',
      mbError,
      MB_OK,
      IDOK);
    Result := False;
  end;
end;
