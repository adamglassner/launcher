; Inno Setup script for Ironite Launcher
; Build: run ..\build.ps1, or compile this file with Inno Setup 6 (ISCC.exe) after publishing.
;
; Updates: the launcher downloads IroniteLauncher-Setup-<version>.exe from its GitHub release and runs it
; with /SILENT /RELAUNCH=1 (only the progress window; the launcher is opened again when it's done).
; Launcher 2.x (the Go version) runs it with /AUTO /relaunch instead, which this script also understands,
; and setup replaces the 2.x install (made with NSIS) in place, keeping games, saves and play stats.

#define MyAppName "Ironite Launcher"
#ifndef MyAppVersion
  #define MyAppVersion "3.0.0"
#endif
#define MyAppPublisher "Ironite"
#define MyAppExeName "IroniteLauncher.exe"
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif

[Setup]
AppId={{7A3F1C2E-4B5D-4E8F-9A1B-2C3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (c) 2026 Ironite. All rights reserved.
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=Copyright (c) 2026 Ironite. All rights reserved.
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
; The same folder launcher 2.x was installed in, so it's replaced in place.
DefaultDirName={autopf}\Ironite\Launcher
DefaultGroupName=Ironite
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=IroniteLauncher-Setup-{#MyAppVersion}
SetupIconFile=..\assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
WizardImageFile=wizard-large-1.bmp,wizard-large-2.bmp
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-110.bmp
Compression=lzma2/max
SolidCompression=yes
; Files in use (a launcher that's still closing) are closed for the update.
CloseApplications=yes
RestartApplications=no
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nThe launcher downloads Ironite games and keeps them, and itself, up to date.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: not IsRelaunch
; An update started by the launcher: open it again when done, as the signed-in user (not as administrator).
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runasoriginaluser; Check: IsRelaunch

[UninstallRun]
; Close the launcher and any Ironite game that's running, so their files can be removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM IroniteLauncher.exe /IM Klondike.exe /IM SolarSiege.exe /IM OldMaid.exe /IM Euchre.exe /IM Hearts.exe /IM Cribbage.exe /IM GinRummy.exe /IM Spades.exe /IM MapleLaneFinds.exe /IM Snugfit.exe /IM TexasHoldem.exe /IM Blackjack.exe /IM GoFish.exe /IM Chess.exe /IM Checkers.exe /IM Mahjong.exe /IM Keyhole.exe /IM RewindLife.exe /IM CarnivalConstructor.exe /IM AppalachianTrail.exe"; Flags: runhidden waituntilterminated; RunOnceId: "CloseGames"

[UninstallDelete]
; Games the launcher downloaded (saves, settings and play stats in the user's profile are kept).
Type: filesandordirs; Name: "{localappdata}\Ironite\Launcher"
Type: dirifempty; Name: "{localappdata}\Ironite"

[Code]
function HasParam(const Name: String): Boolean;
var
  i: Integer;
begin
  Result := False;
  for i := 1 to ParamCount do
    if CompareText(ParamStr(i), Name) = 0 then
      Result := True;
end;

{ /AUTO (launcher 2.x's updater): no questions, just the progress window. }
function IsAuto: Boolean;
begin
  Result := HasParam('/AUTO');
end;

{ Open the launcher again after an update it started: /RELAUNCH=1 (3.x) or /relaunch (2.x). }
function IsRelaunch: Boolean;
begin
  Result := (ExpandConstant('{param:RELAUNCH|0}') = '1') or HasParam('/relaunch');
end;

function InitializeSetup: Boolean;
var
  i: Integer;
begin
  Result := True;
  { A launcher that started this update is closing: give it a moment to let go. }
  for i := 1 to 60 do
  begin
    if not CheckForMutexes('IroniteLauncher') then
      Exit;
    Sleep(250);
  end;
  while CheckForMutexes('IroniteLauncher') and not WizardSilent do
  begin
    if MsgBox('Ironite Launcher is running. Close it (and any game you started from it), then click OK to continue.',
              mbInformation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := IsAuto;
end;

{ Launcher 2.x was installed with NSIS into the same folder. Remove what it left that this version doesn't
  use: its uninstaller (which would also delete the downloaded games), extra files, and its registry entry.
  The games, saves and play stats are kept. }
procedure RemoveOldInstall;
var
  Dir: String;
begin
  Dir := ExpandConstant('{app}');
  if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\IroniteLauncher', 'InstallLocation', Dir) then
    Log('Replacing launcher 2.x in ' + Dir);
  DeleteFile(AddBackslash(Dir) + 'Uninstall.exe');
  DeleteFile(AddBackslash(Dir) + 'CHANGELOG.txt');
  DeleteFile(AddBackslash(Dir) + 'README.txt');
  DeleteFile(AddBackslash(Dir) + 'icon.ico');
  if CompareText(RemoveBackslash(Dir), RemoveBackslash(ExpandConstant('{app}'))) <> 0 then
  begin
    DeleteFile(AddBackslash(Dir) + 'IroniteLauncher.exe');
    RemoveDir(Dir);
  end;
  RegDeleteKeyIncludingSubkeys(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\IroniteLauncher');
  RegDeleteKeyIncludingSubkeys(HKLM64, 'Software\Ironite\Launcher');
  RegDeleteKeyIfEmpty(HKLM64, 'Software\Ironite');
  DeleteFile(ExpandConstant('{commonprograms}\Ironite\Uninstall Ironite Launcher.lnk'));
  DeleteFile(ExpandConstant('{commonprograms}\Ironite\Ironite Launcher.lnk'));
  DeleteFile(ExpandConstant('{commondesktop}\Ironite Launcher.lnk'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemoveOldInstall;
end;
