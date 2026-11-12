; Inno Setup script for the unpackaged Sillfall app (ADR-0018).
; Built by tools/ci/installer.ps1, which passes the defines below. Requires Inno Setup 6.3 or newer (arm64).
;
;   iscc /DAppVersion=0.1.0 /DArch=x64 /DPublishDir=<publish folder> /O<output folder> tools/installer/Sillfall.iss

#ifndef AppVersion
  #error AppVersion is required (for example /DAppVersion=0.1.0).
#endif
#ifndef Arch
  #error Arch is required (x64 or arm64).
#endif
#ifndef PublishDir
  #error PublishDir is required (the folder dotnet publish wrote).
#endif

#define AppName "Sillfall"
#define AppExeName "Sillfall.App.exe"
#define AppPublisher "Sillfall project"
#define AppUrl "https://github.com/TeslaV0D/Sillfall"
#define UriScheme "sillfall"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
; The id identifies the installation for updates and uninstall. Never change it.
AppId={{62E2C09E-EACC-4D42-87E2-6B1CFF3237F2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
; Per-user by default, like the app's own registry entries (HKCU). An admin can still install for all users.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19041
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\src\Sillfall.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}-{#Arch}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The app holds this mutex while it runs (SingleInstance). Setup asks to close it before replacing files.
AppMutex=Local\Sillfall.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Scene links. The app writes the same key on every start (Integration.RegisterProtocol); setup adds it right away.
Root: HKA; Subkey: "Software\Classes\{#UriScheme}"; ValueType: string; ValueName: ""; ValueData: "URL:{#AppName} scene"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#UriScheme}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\{#UriScheme}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// The app writes the protocol and the autostart entry for the current user, whatever the install scope.
// Remove both on uninstall. Settings and the collection in %LOCALAPPDATA%\Sillfall stay (docs/privacy.md).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\{#UriScheme}');
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#AppName}');
  end;
end;
