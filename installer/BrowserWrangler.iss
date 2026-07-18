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
Filename: "{app}\BrowserWrangler.exe"; Description: "Open Browser Wrangler"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\BrowserWrangler.exe"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "UnregisterBrowser"
