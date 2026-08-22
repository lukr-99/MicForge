; MicForge installer script (Inno Setup 6)
; Per-user install: no admin/UAC required, installs to %LocalAppData%\Programs\MicForge,
; adds a Start-menu shortcut, and registers an uninstaller in "Installed apps".

#define MyAppName "MicForge"
#define MyAppVersion "1.8.0"
#define MyAppPublisher "lukr-99"
#define MyAppExeName "MicForge.exe"
#define MyAppURL "https://github.com/lukr-99/MicForge"

; Path to the self-contained publish output (passed in via ISCC /D, with a fallback).
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{EA83B970-0D1A-41E4-8BA4-C9A547F9338A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\MicForge
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir=.
OutputBaseFilename=MicForge-Setup-{#MyAppVersion}
SetupIconFile=..\MicForge.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion=1.8.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
LicenseFile=..\LICENSE.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
