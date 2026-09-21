#define AppName "Codex Meter"
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputPath
  #error OutputPath is required
#endif

[Setup]
AppId={{C884F6B3-06B6-47D4-9843-19F2D157B8F5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Banana Metal
AppPublisherURL=https://bananametal-dev.github.io/
DefaultDirName={localappdata}\Programs\Codex Meter
DefaultGroupName=Codex Meter
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputPath}
OutputBaseFilename=CodexMeter-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\CodexMeter.exe
AppMutex=Local\CodexMeterV1
CloseApplications=no
RestartApplications=no
SetupLogging=no
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Codex + Meter のデスクトップショートカットを作成"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "state\*,test-artifacts\*,*.pdb"

[Icons]
Name: "{group}\Codex + Meter"; Filename: "{app}\CodexMeter.exe"; Parameters: "--with-codex"; WorkingDir: "{app}"
Name: "{group}\Codex Meter"; Filename: "{app}\CodexMeter.exe"; WorkingDir: "{app}"
Name: "{group}\Codex Meter のアンインストール"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Codex + Meter"; Filename: "{app}\CodexMeter.exe"; Parameters: "--with-codex"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\CodexMeter.exe"; Description: "Codex Meterを起動"; Flags: nowait postinstall skipifsilent
