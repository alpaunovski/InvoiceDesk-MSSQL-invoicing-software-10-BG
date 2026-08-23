; InvoiceDesk Inno Setup Script
; Complete 1-Click Installer with Automatic Dependency Detection & Silent Installation
; 
; Supported Dependencies:
;   1. .NET 8.0 Desktop Runtime (x64)
;   2. Microsoft Edge WebView2 Runtime (Evergreen)
;   3. Microsoft SQL Server LocalDB (2019/2022 x64)
;
; How to build:
;   1. Publish the WPF application:
;        dotnet publish ..\InvoiceDesk\InvoiceDesk.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=false
;   2. (Optional) Place prerequisite installers in Payloads/ for fully offline setup:
;        - Payloads\SqlLocalDB.msi
;        - Payloads\MicrosoftEdgeWebview2Setup.exe
;        - Payloads\windowsdesktop-runtime-8-win-x64.exe
;   3. Compile this script using Inno Setup Compiler (ISCC.exe InvoiceDesk.iss).

#define MyAppName "InvoiceDesk"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "InvoiceDesk"
#define MyAppExeName "InvoiceDesk.exe"

; Relative paths based on repository directory layout
#define MySourceDir "..\..\InvoiceDesk\bin\Release\net8.0-windows\win-x64\publish"
#define MyLicenseFile "..\..\LICENSE"

[Setup]
AppId={{6D828B35-29F8-4EF2-96F5-02C754C0C6D9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=InvoiceDesk-setup
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed=x64compatible x64
ArchitecturesInstallIn64BitMode=x64compatible x64
PrivilegesRequired=admin
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Main application publish output
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyLicenseFile}"; DestDir: "{app}"; Flags: ignoreversion

; Optional pre-bundled prerequisite payloads (bundled into setup if placed in Payloads/ at compile time)
Source: "Payloads\SqlLocalDB.msi"; DestDir: "{tmp}"; DestName: "SqlLocalDB.msi"; Flags: ignoreversion deleteafterinstall skipifdoesntexist
Source: "Payloads\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: ignoreversion deleteafterinstall skipifdoesntexist
Source: "Payloads\windowsdesktop-runtime-8-win-x64.exe"; DestDir: "{tmp}"; DestName: "windowsdesktop-runtime-8-win-x64.exe"; Flags: ignoreversion deleteafterinstall skipifdoesntexist

[Dirs]
Name: "{localappdata}\InvoiceDesk"; Flags: uninsalwaysuninstall
Name: "{localappdata}\InvoiceDesk\Exports"; Flags: uninsalwaysuninstall
Name: "{localappdata}\InvoiceDesk\Exports\backups"; Flags: uninsalwaysuninstall
Name: "{localappdata}\InvoiceDesk\Exports\signed"; Flags: uninsalwaysuninstall
Name: "{localappdata}\InvoiceDesk\logs"; Flags: uninsalwaysuninstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 1. Install Microsoft .NET 8 Desktop Runtime (silent) if missing
Filename: "{tmp}\windowsdesktop-runtime-8-win-x64.exe"; Parameters: "/q /norestart"; StatusMsg: "Installing Microsoft .NET 8 Desktop Runtime (silent)..."; Flags: runhidden; Check: DotNet8NeedsInstall

; 2. Install Microsoft Edge WebView2 Runtime (silent) if missing
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime (silent)..."; Flags: runhidden; Check: WebView2NeedsInstall

; 3. Install Microsoft SQL Server LocalDB (silent) if missing
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\SqlLocalDB.msi"" IACCEPTSQLLOCALDBLICENSETERMS=YES /qn /norestart"; StatusMsg: "Installing SQL Server LocalDB (silent)..."; Flags: runhidden; Check: LocalDbNeedsInstall

; 4. Create and start MSSQLLocalDB instance
Filename: "cmd.exe"; Parameters: "/c sqllocaldb create MSSQLLocalDB & sqllocaldb start MSSQLLocalDB"; StatusMsg: "Initializing SQL Server LocalDB instance..."; Flags: runhidden; Check: LocalDbInstalled

; 5. Launch application
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Detect if SQL Server LocalDB is installed (checks registry for LocalDB 2016 - 2022)
function LocalDbInstalled(): Boolean;
var
  SubKeys: TArrayOfString;
begin
  Result :=
    RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\16.0') or
    RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\15.0') or
    RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\14.0') or
    RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\13.0') or
    RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\16.0') or
    RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\15.0') or
    RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\14.0') or
    RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\13.0');

  if not Result then
  begin
    if RegGetSubkeyNames(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions', SubKeys) or
       RegGetSubkeyNames(HKLM, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions', SubKeys) then
    begin
      Result := (GetArrayLength(SubKeys) > 0);
    end;
  end;
end;

// Detect if WebView2 Evergreen Runtime is installed
function WebView2Installed(): Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
     RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
     RegQueryStringValue(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
     RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
      Result := True;
  end;
end;

// Detect if .NET 8 Desktop Runtime (x64) is installed
function DotNet8DesktopInstalled(): Boolean;
var
  SubKeys: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', SubKeys) or
     RegGetSubkeyNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', SubKeys) then
  begin
    for I := 0 to GetArrayLength(SubKeys) - 1 do
    begin
      if Pos('8.', SubKeys[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  if not Result then
  begin
    Result := DirExists(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.0')) or
              DirExists(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.0'));
  end;
end;

// Execution check functions for [Run] entries
function LocalDbNeedsInstall(): Boolean;
begin
  Result := (not LocalDbInstalled()) and FileExists(ExpandConstant('{tmp}\SqlLocalDB.msi'));
end;

function WebView2NeedsInstall(): Boolean;
begin
  Result := (not WebView2Installed()) and FileExists(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'));
end;

function DotNet8NeedsInstall(): Boolean;
begin
  Result := (not DotNet8DesktopInstalled()) and FileExists(ExpandConstant('{tmp}\windowsdesktop-runtime-8-win-x64.exe'));
end;

// PowerShell download fallback
function DownloadWithPowerShell(const Url, TargetFile: String): Boolean;
var
  ResultCode: Integer;
  CmdArgs: String;
begin
  CmdArgs := Format('/c powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object System.Net.WebClient).DownloadFile(''%s'', ''%s'')" ', [Url, TargetFile]);
  Result := Exec('cmd.exe', CmdArgs, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) and FileExists(TargetFile);
end;

// Check prerequisites and queue automatic download if missing on target machine
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  DownloadCount: Integer;
  LocalDbPath, WebViewPath, DotNetPath: String;
begin
  Result := '';
  DownloadCount := 0;
  LocalDbPath := ExpandConstant('{tmp}\SqlLocalDB.msi');
  WebViewPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
  DotNetPath  := ExpandConstant('{tmp}\windowsdesktop-runtime-8-win-x64.exe');

  // Queue SQL LocalDB if missing
  if not LocalDbInstalled() then
  begin
    if not FileExists(LocalDbPath) then
    begin
      Log('SQL LocalDB is missing. Queueing download for SqlLocalDB.msi...');
      DownloadTemporaryFile(
        'https://download.microsoft.com/download/7/c/1/7c14e92d-a6ca-4c3a-ab45-b4fb6c1f1f50/SqlLocalDB.msi',
        'SqlLocalDB.msi',
        'Microsoft SQL Server LocalDB 2019 (x64)',
        nil
      );
      DownloadCount := DownloadCount + 1;
    end;
  end;

  // Queue Edge WebView2 Runtime if missing
  if not WebView2Installed() then
  begin
    if not FileExists(WebViewPath) then
    begin
      Log('WebView2 Runtime is missing. Queueing download for MicrosoftEdgeWebview2Setup.exe...');
      DownloadTemporaryFile(
        'https://msedge.sf.dl.delivery.mp.microsoft.com/filestream/MicrosoftEdgeWebview2Setup.exe',
        'MicrosoftEdgeWebview2Setup.exe',
        'Microsoft Edge WebView2 Evergreen Bootstrapper',
        nil
      );
      DownloadCount := DownloadCount + 1;
    end;
  end;

  // Queue .NET 8 Desktop Runtime if missing
  if not DotNet8DesktopInstalled() then
  begin
    if not FileExists(DotNetPath) then
    begin
      Log('.NET 8 Desktop Runtime is missing. Queueing download for windowsdesktop-runtime-8-win-x64.exe...');
      DownloadTemporaryFile(
        'https://download.visualstudio.microsoft.com/download/pr/45075678-8ed6-4993-80e9-b5472e3914a1/24fb2fce77e1bf35eb4ee2886f4a7c06/windowsdesktop-runtime-8.0.12-win-x64.exe',
        'windowsdesktop-runtime-8-win-x64.exe',
        'Microsoft .NET 8.0 Desktop Runtime (x64)',
        nil
      );
      DownloadCount := DownloadCount + 1;
    end;
  end;

  // Perform downloads if any prerequisite is missing
  if DownloadCount > 0 then
  begin
    try
      DownloadTemporaryFiles();
    except
      Log('Inno built-in download failed: ' + GetExceptionMessage + '. Attempting PowerShell fallback downloads...');

      if (not LocalDbInstalled()) and (not FileExists(LocalDbPath)) then
        DownloadWithPowerShell('https://download.microsoft.com/download/7/c/1/7c14e92d-a6ca-4c3a-ab45-b4fb6c1f1f50/SqlLocalDB.msi', LocalDbPath);

      if (not WebView2Installed()) and (not FileExists(WebViewPath)) then
        DownloadWithPowerShell('https://msedge.sf.dl.delivery.mp.microsoft.com/filestream/MicrosoftEdgeWebview2Setup.exe', WebViewPath);

      if (not DotNet8DesktopInstalled()) and (not FileExists(DotNetPath)) then
        DownloadWithPowerShell('https://download.visualstudio.microsoft.com/download/pr/45075678-8ed6-4993-80e9-b5472e3914a1/24fb2fce77e1bf35eb4ee2886f4a7c06/windowsdesktop-runtime-8.0.12-win-x64.exe', DotNetPath);

      if ((not LocalDbInstalled()) and (not FileExists(LocalDbPath))) or
         ((not WebView2Installed()) and (not FileExists(WebViewPath))) or
         ((not DotNet8DesktopInstalled()) and (not FileExists(DotNetPath))) then
      begin
        Result := 'Failed to download required setup dependencies.' + #13#10 +
                  'Please check your internet connection or place payload installers into tools/installer/Payloads/.';
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Log('InvoiceDesk Setup Initialized.');
  Log('Prerequisite evaluation:');
  if LocalDbInstalled() then Log(' - SQL LocalDB: Installed') else Log(' - SQL LocalDB: Missing (will install)');
  if WebView2Installed() then Log(' - WebView2 Runtime: Installed') else Log(' - WebView2 Runtime: Missing (will install)');
  if DotNet8DesktopInstalled() then Log(' - .NET 8 Desktop Runtime: Installed') else Log(' - .NET 8 Desktop Runtime: Missing (will install)');
  Result := True;
end;
