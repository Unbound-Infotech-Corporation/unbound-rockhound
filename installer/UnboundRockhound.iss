; Unbound Rockhound — branded Windows x64 setup (Inno Setup 6+)
; Build: scripts\Build-Installer.ps1  (or Package-Release.ps1 -BuildSetup)

#define MyAppName "Unbound Rockhound"
#define MyAppVersion "0.6.0"
#define MyAppPublisher "Unbound Infotech Corporation"
#define MyAppPublisherShort "Unbound Infotech"
#define MyAppURL "https://unboundinfotech.com/"
#define MyAppSupport "support@unboundinfotech.com"
#define MyAppExeName "UnboundRockhound.exe"
#define MyAppIconName "AppIcon.ico"
#define MyAppId "{{A7C4E2B1-9F3D-4A8C-B5E6-1D2F3A4B5C6D}"
#define MyAppCopyright "Copyright (C) 2026 Unbound Infotech Corporation"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright={#MyAppCopyright}
AppMutex=UnboundRockhound_SingleInstance_Setup
DefaultDirName={autopf}\{#MyAppPublisherShort}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
AllowNoIcons=yes
LicenseFile=..\EULA-DISCLAIMER.md
InfoBeforeFile=WELCOME.txt
OutputDir=..\dist
OutputBaseFilename=UnboundRockhound-Setup-{#MyAppVersion}
SetupIconFile=..\src\GeoMineralTrace.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\App\{#MyAppIconName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright={#MyAppCopyright}
VersionInfoProductName={#MyAppName}
VersionInfoProductTextVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}.0
VersionInfoDescription={#MyAppName} Setup
WizardStyle=modern
WizardSizePercent=120,120
WizardImageFile=assets\WizardImage.bmp
WizardSmallImageFile=assets\WizardSmallImage.bmp
WizardImageStretch=yes
ShowLanguageDialog=no
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.17763
ChangesAssociations=no
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppTitle=Install {#MyAppName}
SetupWindowTitle={#MyAppName} Setup — {#MyAppPublisherShort}
WelcomeLabel1=Welcome to the [name] Setup Wizard
WelcomeLabel2=This will install [name/ver] on your computer.%n%nWe recommend closing other applications before continuing so the installer can update files without restarting Windows.
FinishedLabel=Setup has finished installing [name] on your computer. The application may be launched by selecting the installed shortcuts or by choosing Launch below.
ClickFinish=Click Finish to exit Setup.
ConfirmUninstall=Are you sure you want to completely remove %1 and all of its components?%n%nYour local research data under %%LocalAppData%%\UnboundRockhound\ will be kept.

[CustomMessages]
UnboundExtraIcons=Shortcuts
UnboundDesktopIcon=Create a &Desktop shortcut (recommended)
UnboundWebsite=Unbound Infotech website
LaunchAfterInstall=Launch Unbound Rockhound

[Tasks]
Name: "desktopicon"; Description: "{cm:UnboundDesktopIcon}"; GroupDescription: "{cm:UnboundExtraIcons}"; Flags: checkedonce

[Files]
; Application binaries (self-contained publish)
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\App\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
; Explicit product icon for Start Menu / Desktop / Uninstall (never rely on exe extraction alone)
Source: "..\src\GeoMineralTrace.App\Assets\AppIcon.ico"; DestDir: "{app}\App"; DestName: "AppIcon.ico"; Flags: ignoreversion
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\knowledge\*"; DestDir: "{app}\knowledge"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\data\*"; DestDir: "{app}\data"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\updates\*"; DestDir: "{app}\updates"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\EULA-DISCLAIMER.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\INSTALL.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\dist\stage-zip\UnboundRockhound-Windows-x64\Launch Unbound Rockhound.bat"; DestDir: "{app}"; DestName: "Launch Unbound Rockhound.bat"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
; Start Menu — branded product icon
Name: "{group}\{#MyAppName}"; Filename: "{app}\App\{#MyAppExeName}"; WorkingDir: "{app}\App"; IconFilename: "{app}\App\{#MyAppIconName}"; IconIndex: 0; Comment: "Forensic geolocation & field research"
Name: "{group}\Read Install Notes"; Filename: "{app}\INSTALL.txt"; Comment: "Requirements and quick start"
Name: "{group}\{cm:UnboundWebsite}"; Filename: "{#MyAppURL}"; Comment: "{#MyAppPublisher}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\App\{#MyAppIconName}"; IconIndex: 0
; Desktop — same branded icon (checked by default)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\App\{#MyAppExeName}"; Tasks: desktopicon; WorkingDir: "{app}\App"; IconFilename: "{app}\App\{#MyAppIconName}"; IconIndex: 0; Comment: "Unbound Rockhound"

[Run]
Filename: "{app}\App\{#MyAppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function InitializeSetup: Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Ensure icon file landed even if a prior partial install existed
    if not FileExists(ExpandConstant('{app}\App\{#MyAppIconName}')) then
      Log('WARNING: AppIcon.ico missing after install — shortcuts may fall back to exe icon.');
  end;
end;
