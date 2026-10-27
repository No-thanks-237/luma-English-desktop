#define AppVersion "0.1.5"
#include "Runtime.iss"

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
Filename: "{app}\Luma.exe"; Description: "Launch Luma"; Flags: nowait postinstall skipifsilent; Check: not IsLumaUpdate
Filename: "{app}\Luma.exe"; Flags: nowait; Check: IsLumaUpdate

[Code]
var
  RuntimeDownloadPage: TDownloadWizardPage;
  RuntimeNeedsRestart: Boolean;

function IsLumaUpdate: Boolean;
begin
  Result := ExpandConstant('{param:LUMAUPDATE|0}') = '1';
end;

function HasVCRuntime: Boolean;
var
  Installed, Major, Minor: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Major', Major) and
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Minor', Minor);
  if Result then Result := (Installed = 1) and ((Major > 14) or ((Major = 14) and (Minor >= 29)));
end;

procedure InitializeWizard;
begin
  RuntimeDownloadPage := CreateDownloadPage('OCR runtime', 'Downloading Microsoft Visual C++ x64 Runtime...', nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if HasVCRuntime then begin
    Log('Luma: compatible Visual C++ x64 runtime detected; prerequisite download skipped.');
    exit;
  end;
  if WizardSilent then begin
    Result := 'Microsoft Visual C++ x64 Runtime is missing. Run interactive setup or install it from https://aka.ms/vc14/vc_redist.x64.exe first.';
    exit;
  end;
  if MsgBox('OCR needs Microsoft Visual C++ x64 Runtime. Setup will download it from Microsoft and ask Windows for administrator approval. Continue?', mbConfirmation, MB_YESNO) <> IDYES then begin
    Result := 'OCR runtime installation was cancelled. Install the Microsoft x64 Runtime and retry.';
    exit;
  end;
  RuntimeDownloadPage.Clear;
  RuntimeDownloadPage.Add('{#VCRuntimeUrl}', 'vc_redist.x64.exe', '{#VCRuntimeSHA256}');
  RuntimeDownloadPage.Show;
  try
    try
      RuntimeDownloadPage.Download;
    except
      Result := 'Unable to download the OCR runtime. Check your network or install it manually from https://aka.ms/vc14/vc_redist.x64.exe.';
      Log(GetExceptionMessage);
    end;
  finally
    RuntimeDownloadPage.Hide;
  end;
  if Result <> '' then exit;
  if not ShellExec('runas', ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /passive /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then begin
    Result := 'Windows did not start the OCR runtime installer: ' + SysErrorMessage(ResultCode);
    exit;
  end;
  if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638) then begin
    Result := Format('Microsoft runtime setup returned error %d. Install the x64 runtime manually and retry.', [ResultCode]);
    exit;
  end;
  RuntimeNeedsRestart := ResultCode = 3010;
  if not HasVCRuntime then Result := 'Microsoft Visual C++ x64 Runtime was not detected after setup. Restart Windows if requested, then retry.';
end;

function NeedRestart: Boolean;
begin
  Result := RuntimeNeedsRestart;
end;
