; Inno-Setup-Skript für den Lu-Mail-Installer (Lu-Mail-<Version>-Windows-<Arch>-Setup.exe).
; Aufruf (siehe windows/installer/build-installer.ps1):
;   iscc /DAppVersion=0.2.0 /DArch=x64 /DSourceDir=..\..\publish\win-x64 /DOutputDir=..\..\dist LuMail.iss
;
; Installiert pro Benutzer (keine Administratorrechte nötig) – so kann der Auto-Updater
; neue Versionen still im Hintergrund einspielen.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish\win-" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

[Setup]
AppId={{6E3B1C52-7A0F-4E5B-9C1D-4C2A8F1B9D21}
AppName=Lu-Mail
AppVersion={#AppVersion}
AppVerName=Lu-Mail {#AppVersion}
AppPublisher=Luna
AppPublisherURL=https://github.com/Luna-OS/Lu-Mail
AppSupportURL=https://github.com/Luna-OS/Lu-Mail/issues
AppUpdatesURL=https://github.com/Luna-OS/Lu-Mail/releases
DefaultDirName={localappdata}\Programs\Lu-Mail
DefaultGroupName=Lu-Mail
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=Lu-Mail-{#AppVersion}-Windows-{#Arch}-Setup
SetupIconFile=..\src\LuMail.App\Assets\LuMail.ico
UninstallDisplayIcon={app}\LuMail.exe
UninstallDisplayName=Lu-Mail
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Alte Programmdateien entfernen, damit keine Reste früherer Versionen bleiben.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{autoprograms}\Lu-Mail"; Filename: "{app}\LuMail.exe"
Name: "{autodesktop}\Lu-Mail"; Filename: "{app}\LuMail.exe"; Tasks: desktopicon

[Run]
; Läuft auch bei stiller Installation durch den Auto-Updater – außer mit /RELAUNCH=0
; (Update beim Beenden von Lu-Mail).
Filename: "{app}\LuMail.exe"; Description: "{cm:LaunchProgram,Lu-Mail}"; Flags: nowait postinstall; Check: ShouldLaunch

[Code]
function ShouldLaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|1}') <> '0';
end;
