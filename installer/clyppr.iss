; Clyppr installer (Inno Setup 6)
; Per-user install (no UAC), bundles ffmpeg, optional run-at-startup.
; Build via scripts\package.ps1 which passes SourceDir / AppVersion / OutputDir.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{A6E3D5C2-9B4F-4E8A-BC17-7F2D3E9A1C64}
AppName=Clyppr
AppVersion={#AppVersion}
AppVerName=Clyppr {#AppVersion}
AppPublisher=Clyppr
AppPublisherURL=https://clyppr.com
AppSupportURL=https://github.com/izoose/clyppr/issues
AppUpdatesURL=https://github.com/izoose/clyppr/releases
DefaultDirName={autopf}\Clyppr
DefaultGroupName=Clyppr
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=Clyppr-Setup-x64
SetupIconFile=..\src\Clipper.App\Assets\clipper.ico
UninstallDisplayIcon={app}\Clyppr.exe
UninstallDisplayName=Clyppr
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startup"; Description: "Start Clyppr when I sign in to Windows (runs in the tray)"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\Clyppr.exe";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\ffmpeg.exe";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\ffprobe.exe";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE.txt";             DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.md";  DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\ffmpeg-LICENSE.txt";      DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\Clyppr";        Filename: "{app}\Clyppr.exe"
Name: "{userdesktop}\Clyppr";  Filename: "{app}\Clyppr.exe"; Tasks: desktopicon

[Registry]
; Matches StartupManager's HKCU Run value ("path" --tray); removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "Clyppr"; ValueData: """{app}\Clyppr.exe"" --tray"; \
    Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\Clyppr.exe"; Description: "Launch Clyppr"; Flags: nowait postinstall skipifsilent

; NOTE: user clips live under %USERPROFILE%\Videos\Clipper and the library/settings under
; %LocalAppData%\Clipper (created by the app, never installed here), so uninstall leaves
; the user's library untouched by design.
