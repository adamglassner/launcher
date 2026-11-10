; Ironite Launcher - Windows installer
; Build: makensis -DVERSION=1.0.0 -DSTAGE=<staging dir> -DOUTFILE=<setup.exe> setup.nsi

Unicode true
SetCompressor /SOLID lzma
ManifestDPIAware true
RequestExecutionLevel admin

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!define PRODUCT     "Ironite Launcher"
!define PUBLISHER   "Ironite"
!define EXE         "IroniteLauncher.exe"
!define UNINST_KEY  "Software\Microsoft\Windows\CurrentVersion\Uninstall\IroniteLauncher"
!define APP_KEY     "Software\Ironite\Launcher"
; Earlier releases were installed under a different name; setup replaces them.
!define OLD_PRODUCT "Ironite Casual Game Compilation"
!define OLD_EXE     "IroniteCGC.exe"
!define OLD_UNINST  "Software\Microsoft\Windows\CurrentVersion\Uninstall\IroniteCasualGameCompilation"
!define OLD_APP_KEY "Software\Ironite\Casual Game Compilation"

Name "${PRODUCT}"
Caption "${PRODUCT} ${VERSION} Setup"
BrandingText "Ironite Launcher ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\Ironite\Launcher"
InstallDirRegKey HKLM "${APP_KEY}" "InstallDir"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName"     "${PRODUCT}"
VIAddVersionKey "CompanyName"     "${PUBLISHER}"
VIAddVersionKey "FileDescription" "${PRODUCT} Setup"
VIAddVersionKey "FileVersion"     "${VERSION}"
VIAddVersionKey "ProductVersion"  "${VERSION}"
VIAddVersionKey "LegalCopyright"  "Copyright (c) 2026 Ironite. All rights reserved."

!ifdef PLUGINS
  !addplugindir /x86-unicode "${PLUGINS}\x86-unicode"
!endif

!include "MUI2.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"

!define MUI_ICON   "${STAGE}\icon.ico"
!define MUI_UNICON "${STAGE}\icon.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP   "${STAGE}\wizard.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${STAGE}\wizard.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP   "${STAGE}\header.bmp"
!define MUI_HEADERIMAGE_UNBITMAP "${STAGE}\header.bmp"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TITLE "Welcome to the ${PRODUCT} ${VERSION} setup"
!define MUI_WELCOMEPAGE_TEXT "This will install Ironite Launcher on your computer.$\r$\n$\r$\nThe launcher downloads Ironite games and keeps them, and itself, up to date.$\r$\n$\r$\nClick Next to continue."
; /AUTO (used by the launcher's updater, like Bytehawk's /SILENT): no questions,
; just the progress window, which closes by itself when the update is installed.
Var Auto

!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipIfAuto
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipIfAuto
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TITLE "Ready to play"
!define MUI_FINISHPAGE_TEXT "${PRODUCT} ${VERSION} is installed. You'll find it on your desktop and in the Start menu."
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Open the launcher now"
!define MUI_FINISHPAGE_RUN_FUNCTION RunLauncher
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\CHANGELOG.txt"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Show the changelog"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipIfAuto
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function SkipIfAuto
  ${If} $Auto == 1
    Abort ; skip this page
  ${EndIf}
FunctionEnd

Function .onInit
  StrCpy $Auto 0
  ${GetParameters} $1
  ClearErrors
  ${GetOptions} $1 "/AUTO" $2
  ${IfNot} ${Errors}
    StrCpy $Auto 1
    SetAutoClose true
  ${EndIf}
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${PRODUCT} requires 64-bit Windows 10 or 11."
    Abort
  ${EndIf}
  SetRegView 64
  ReadRegStr $0 HKLM "${UNINST_KEY}" "DisplayVersion"
  ${If} $0 == ""
    ReadRegStr $0 HKLM "${OLD_UNINST}" "DisplayVersion"
  ${EndIf}
  ${If} $0 != ""
  ${AndIf} $0 != "${VERSION}"
  ${AndIf} $Auto != 1
    ; The launcher's own updater runs setup with /AUTO (or /S): no questions.
    MessageBox MB_OKCANCEL|MB_ICONINFORMATION "${PRODUCT} $0 is installed. Setup will update it to version ${VERSION}." /SD IDOK IDOK +2
    Abort
  ${EndIf}
FunctionEnd

; After an update started by the launcher (/relaunch), open the new launcher again.
Function .onInstSuccess
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/relaunch" $1
  ${IfNot} ${Errors}
    Call RunLauncher
  ${EndIf}
FunctionEnd

Function RunLauncher
  ; Start the launcher as the signed-in user, not as administrator.
  StdUtils::ExecShellAsUser /NOUNLOAD "$INSTDIR\${EXE}" "open" ""
  Pop $0
  ${If} $0 != "ok"
  ${AndIf} $0 != "fallback"
    Exec '"$INSTDIR\${EXE}"' ; couldn't start it as the user: start it anyway
  ${EndIf}
FunctionEnd

Section "Launcher" SecMain
  SectionIn RO
  SetShellVarContext all
  SetRegView 64

  ; Close a running launcher so its file can be replaced.
  nsProcess::_KillProcess "${EXE}"
  Pop $0
  nsProcess::_KillProcess "${OLD_EXE}"
  Pop $0
  Sleep 300

  ; Remove an install from before the rename. Installed games, saves and
  ; play stats in the user's profile are kept; the launcher moves them over.
  ReadRegStr $1 HKLM "${OLD_UNINST}" "InstallLocation"
  ${If} $1 != ""
    Delete "$1\${OLD_EXE}"
    Delete "$1\CHANGELOG.txt"
    Delete "$1\README.txt"
    Delete "$1\icon.ico"
    Delete "$1\Uninstall.exe"
    RMDir  "$1"
    Delete "$SMPROGRAMS\Ironite\${OLD_PRODUCT}.lnk"
    Delete "$SMPROGRAMS\Ironite\Uninstall ${OLD_PRODUCT}.lnk"
    Delete "$DESKTOP\${OLD_PRODUCT}.lnk"
    DeleteRegKey HKLM "${OLD_UNINST}"
    DeleteRegKey HKLM "${OLD_APP_KEY}"
  ${EndIf}

  SetOutPath "$INSTDIR"
  File "${STAGE}\${EXE}"
  File "${STAGE}\CHANGELOG.txt"
  File "${STAGE}\README.txt"
  File "${STAGE}\icon.ico"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\Ironite"
  CreateShortcut "$SMPROGRAMS\Ironite\${PRODUCT}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0
  CreateShortcut "$SMPROGRAMS\Ironite\Uninstall ${PRODUCT}.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\${PRODUCT}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\${EXE}" 0

  WriteRegStr HKLM "${APP_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "${APP_KEY}" "Version"    "${VERSION}"

  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayName"          "${PRODUCT}"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayVersion"       "${VERSION}"
  WriteRegStr   HKLM "${UNINST_KEY}" "Publisher"            "${PUBLISHER}"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayIcon"          "$INSTDIR\${EXE},0"
  WriteRegStr   HKLM "${UNINST_KEY}" "InstallLocation"      "$INSTDIR"
  WriteRegStr   HKLM "${UNINST_KEY}" "UninstallString"      '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "EstimatedSize" ${SIZE_KB}
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  SetRegView 64
  nsProcess::_KillProcess "${EXE}"
  Pop $0
  nsProcess::_KillProcess "Klondike.exe"
  Pop $0
  nsProcess::_KillProcess "SolarSiege.exe"
  Pop $0
  nsProcess::_KillProcess "OldMaid.exe"
  Pop $0
  nsProcess::_KillProcess "Euchre.exe"
  Pop $0
  nsProcess::_KillProcess "Hearts.exe"
  Pop $0
  nsProcess::_KillProcess "Cribbage.exe"
  Pop $0
  nsProcess::_KillProcess "GinRummy.exe"
  Pop $0
  nsProcess::_KillProcess "Spades.exe"
  Pop $0
  nsProcess::_KillProcess "MapleLaneFinds.exe"
  Pop $0
  nsProcess::_KillProcess "Snugfit.exe"
  Pop $0
  nsProcess::_KillProcess "TexasHoldem.exe"
  Pop $0
  nsProcess::_KillProcess "Blackjack.exe"
  Pop $0
  nsProcess::_KillProcess "GoFish.exe"
  Pop $0
  nsProcess::_KillProcess "Chess.exe"
  Pop $0
  nsProcess::_KillProcess "Checkers.exe"
  Pop $0
  nsProcess::_KillProcess "RewindLife.exe"
  Pop $0
  nsProcess::_KillProcess "AppalachianTrail.exe"
  Pop $0
  Sleep 300

  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\CHANGELOG.txt"
  Delete "$INSTDIR\README.txt"
  Delete "$INSTDIR\icon.ico"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir  "$INSTDIR"
  RMDir  "$INSTDIR\.."

  Delete "$SMPROGRAMS\Ironite\${PRODUCT}.lnk"
  Delete "$SMPROGRAMS\Ironite\Uninstall ${PRODUCT}.lnk"
  RMDir  "$SMPROGRAMS\Ironite"
  Delete "$DESKTOP\${PRODUCT}.lnk"

  ; Games the launcher downloaded for the current user (scores and stats are kept).
  SetShellVarContext current
  RMDir /r "$LOCALAPPDATA\Ironite\Launcher"
  RMDir "$LOCALAPPDATA\Ironite"

  DeleteRegKey HKLM "${UNINST_KEY}"
  DeleteRegKey HKLM "${APP_KEY}"
  DeleteRegKey /ifempty HKLM "Software\Ironite"
SectionEnd
