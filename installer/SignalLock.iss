; SignalLock — Inno Setup installer script
; See windows/installer/README.md for build instructions.
; The version is injected by build.ps1 via /DMyAppVersion=...; the fallback
; below only matters when ISCC is invoked manually for ad-hoc tests.

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#define MyAppName     "SignalLock"
#define MyAppPublisher "SignalLock"
#define MyAppURL      "https://signallock.app"
#define MyAppExeName  "SignalLock.exe"

[Setup]
; Immutable — never regenerate. Identifies upgrades / uninstalls of this product.
AppId={{56B5F70A-5A1E-40B5-B9C0-31C437B72F54}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}

DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes

; Wizard asks user up-front: "Install for me only" (no UAC, %LocalAppData%)
; vs "Install for all users" (UAC, Program Files). {autopf} resolves
; correctly for either choice.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=output
OutputBaseFilename=SignalLock-Setup-{#MyAppVersion}-x64
SetupIconFile=..\SignalLock.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

WizardStyle=modern
Compression=lzma2/ultra
SolidCompression=yes
LZMAUseSeparateProcess=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; If SignalLock is running, prompt the user to close it before continuing.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
; Build pipeline: `dotnet publish -c Release -r win-x64 -o publish`
; produces a single self-contained SignalLock.exe in windows\publish.
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
; Final-page checkbox (default checked, hidden in /SILENT mode).
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the auto-start registry entry SignalLock may have written.
; Failure is fine (entry may not exist) — runhidden + don't break uninstall.
Filename: "{cmd}"; Parameters: "/c reg delete ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"" /v ""{#MyAppName}"" /f"; Flags: runhidden

[Code]
function ConfirmDataDeletion(): Boolean;
var
  Msg: string;
begin
  if ActiveLanguage = 'turkish' then
    Msg :=
      '%APPDATA%\{#MyAppName} klasörü ayarlarınızı, güvenilir cihaz kaydınızı' #13#10 +
      've günlük dosyalarını barındırıyor.' #13#10 #13#10 +
      'Bu klasörü de silmek ister misiniz?' #13#10 #13#10 +
      'İleride yeniden kurarsanız ayarlarınızın korunmasını istiyorsanız "Hayır"a tıklayın.'
  else
    Msg :=
      'The %APPDATA%\{#MyAppName} folder holds your settings, trusted device' #13#10 +
      'and log files.' #13#10 #13#10 +
      'Do you want to delete this folder as well?' #13#10 #13#10 +
      'Click "No" to keep your settings for a future reinstall.';

  Result := MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppData: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppData := ExpandConstant('{userappdata}\{#MyAppName}');
    if DirExists(AppData) and ConfirmDataDeletion() then
      DelTree(AppData, True, True, True);
  end;
end;
