#define AppVersion "0.1.4"

[Setup]
AppId={{D84AA353-D4C4-4D14-B40D-664B8D5B0141}
AppName=Luma 拾词
AppVersion={#AppVersion}
AppVerName=Luma 拾词 {#AppVersion}
AppPublisher=Luma Contributors
AppPublisherURL=https://github.com/No-thanks-237/luma-English-desktop
AppSupportURL=https://github.com/No-thanks-237/luma-English-desktop/issues
AppUpdatesURL=https://github.com/No-thanks-237/luma-English-desktop/releases
DefaultDirName={localappdata}\Programs\Luma
DefaultGroupName=Luma 拾词
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\Luma.exe
SetupIconFile=..\src\Assets\luma.ico
OutputDir=..\dist
OutputBaseFilename=Luma-{#AppVersion}-Setup-x64
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\Luma.Desktop.01
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\使用说明.md"; DestDir: "{app}"
Source: "..\第三方组件.md"; DestDir: "{app}"
Source: "..\验收记录.md"; DestDir: "{app}"

[Icons]
Name: "{group}\Luma 拾词"; Filename: "{app}\Luma.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Luma"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Luma 拾词"; Filename: "{app}\Luma.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Luma.exe"; Description: "Launch Luma"; Flags: nowait postinstall skipifsilent
