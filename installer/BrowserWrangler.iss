; Browser Wrangler - per-user Inno Setup installer (winget-friendly).
; Compile: ISCC.exe /DAppVersion=YYYY.MMDD.N /DSourceDir=..\publish\x64 /DArch=x64 BrowserWrangler.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\x64"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{8F398F1B-F8C8-4A30-806E-D0A3FBA8A0D3}
AppName=Browser Wrangler
AppVersion={#AppVersion}
AppPublisher=Damian Hickey
AppPublisherURL=https://github.com/damianh/browser-wrangler
DefaultDirName={userpf}\BrowserWrangler
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=BrowserWrangler-{#AppVersion}-{#Arch}-setup
OutputDir=output
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\BrowserWrangler\Assets\app.ico
UninstallDisplayIcon={app}\BrowserWrangler.exe
CloseApplications=yes
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\Browser Wrangler"; Filename: "{app}\BrowserWrangler.exe"

[Run]
; register as a browser in HKCU so it appears in Default Apps
Filename: "{app}\BrowserWrangler.exe"; Parameters: "--register"; Flags: runhidden
; first install: guide the user through making it the default browser (also on silent/winget installs)
Filename: "{app}\BrowserWrangler.exe"; Parameters: "--first-run"; Flags: nowait runasoriginaluser; Check: IsFirstInstall
; interactive installs still offer the usual "launch now" checkbox on upgrades
Filename: "{app}\BrowserWrangler.exe"; Description: "Open Browser Wrangler"; Flags: nowait postinstall skipifsilent; Check: not IsFirstInstall

[UninstallRun]
Filename: "{app}\BrowserWrangler.exe"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "UnregisterBrowser"

[Code]
var
  FirstInstall: Boolean;

function InitializeSetup(): Boolean;
begin
  { no uninstall entry means this is a fresh install rather than an upgrade }
  FirstInstall := not RegKeyExists(HKEY_CURRENT_USER,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F398F1B-F8C8-4A30-806E-D0A3FBA8A0D3}_is1');
  Result := True;
end;

function IsFirstInstall(): Boolean;
begin
  Result := FirstInstall;
end;

