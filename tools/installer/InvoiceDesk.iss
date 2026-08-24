; InvoiceDesk Inno Setup script
; Configured for clean Windows 10/11 automated dependency installation & setup.
;
; Build steps:
;   1. Publish app: dotnet publish ..\InvoiceDesk\InvoiceDesk.csproj -c Release -r win-x64 --self-contained false -o ..\InvoiceDesk\bin\Release\net8.0-windows\win-x64\publish
;   2. Compile with Inno Setup 6+ Compiler (or run build-installer.ps1).

#define MyAppName "InvoiceDesk"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "InvoiceDesk"
#define MyAppExeName "InvoiceDesk.exe"

; Relative paths from script folder (tools\installer)
#define MySourceDir "..\..\InvoiceDesk\bin\Release\net8.0-windows\win-x64\publish"
#define MyLicenseFile "..\..\LICENSE"

[Setup]
AppId={{6D828B35-29F8-4EF2-96F5-02C754C0C6D9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonpf64}\InvoiceDesk
DefaultGroupName=InvoiceDesk
DisableDirPage=no
DisableProgramGroupPage=yes
OutputBaseFilename=InvoiceDesk-Setup
OutputDir=..\..\executable
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyLicenseFile}"; DestDir: "{app}"; Flags: ignoreversion

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
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// --- Prerequisite Detection Functions ---

function DotNetDesktopRuntimeInstalled(): Boolean;
var
  SubKeys: TArrayOfString;
  I: Integer;
begin
  Result := False;
  // Check x64 .NET Desktop Runtime 8.x registry keys
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', SubKeys) then
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
  if not Result and RegGetSubkeyNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', SubKeys) then
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
end;

function WebView2Installed(): Boolean;
var
  PvVersion: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-6E01-4756-9E28-545B079D89D1}', 'pv', PvVersion) or
     RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-6E01-4756-9E28-545B079D89D1}', 'pv', PvVersion) or
     RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-6E01-4756-9E28-545B079D89D1}', 'pv', PvVersion) then
  begin
    if (PvVersion <> '') and (PvVersion <> '0.0.0.0') then
      Result := True;
  end;
end;

function LocalDbInstalled(): Boolean;
begin
  Result :=
    RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\16.0') or
    RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\15.0') or
    RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\14.0') or
    RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\13.0');
end;

// --- Automated Installation of Prerequisites ---

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  PayloadPath: String;
begin
  Result := '';

  // 1. Install .NET 8 Desktop Runtime (x64) if missing
  if not DotNetDesktopRuntimeInstalled() then
  begin
    WizardForm.StatusLabel.Caption := 'Installing .NET 8 Desktop Runtime...';
    PayloadPath := ExpandConstant('{src}\Payloads\windowsdesktop-runtime-8.0-win-x64.exe');
    if not FileExists(PayloadPath) then
    begin
      PayloadPath := ExpandConstant('{tmp}\windowsdesktop-runtime-8.0-win-x64.exe');
      if not FileExists(PayloadPath) then
      begin
        try
          DownloadTemporaryFile('https://dotnetcli.azureedge.net/dotnet/Runtime/8.0.13/windowsdesktop-runtime-8.0.13-win-x64.exe', 'windowsdesktop-runtime-8.0-win-x64.exe', '', nil);
        except
          Result := 'Failed to download .NET 8 Desktop Runtime. Please verify internet connectivity.';
          Exit;
        end;
      end;
    end;

    if Exec(PayloadPath, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
      if (ResultCode <> 0) and (ResultCode <> 3010) then
        Log(Format('.NET 8 Desktop Runtime returned code %d', [ResultCode]));
    end
    else
    begin
      Result := 'Failed to execute .NET 8 Desktop Runtime installer.';
      Exit;
    end;
  end;

  // 2. Install Microsoft Edge WebView2 Runtime if missing
  if not WebView2Installed() then
  begin
    WizardForm.StatusLabel.Caption := 'Installing Microsoft Edge WebView2 Runtime...';
    PayloadPath := ExpandConstant('{src}\Payloads\MicrosoftEdgeWebview2Setup.exe');
    if not FileExists(PayloadPath) then
    begin
      PayloadPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
      if not FileExists(PayloadPath) then
      begin
        try
          DownloadTemporaryFile('https://msedge.sf.dl.delivery.mp.microsoft.com/filestream/MicrosoftEdgeWebview2Setup.exe', 'MicrosoftEdgeWebview2Setup.exe', '', nil);
        except
          Result := 'Failed to download Microsoft Edge WebView2 Runtime. Please verify internet connectivity.';
          Exit;
        end;
      end;
    end;

    if Exec(PayloadPath, '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
      if (ResultCode <> 0) then
        Log(Format('WebView2 installer returned code %d', [ResultCode]));
    end
    else
    begin
      Result := 'Failed to execute WebView2 installer.';
      Exit;
    end;
  end;

  // 3. Install SQL Server LocalDB 2022 if missing
  if not LocalDbInstalled() then
  begin
    WizardForm.StatusLabel.Caption := 'Installing SQL Server LocalDB 2022...';
    PayloadPath := ExpandConstant('{src}\Payloads\SqlLocalDB.msi');
    if not FileExists(PayloadPath) then
    begin
      PayloadPath := ExpandConstant('{tmp}\SqlLocalDB.msi');
      if not FileExists(PayloadPath) then
      begin
        try
          DownloadTemporaryFile('https://download.microsoft.com/download/7/c/1/7c14e92e-bdcb-4c89-dadc-4a30e32b4952/SqlLocalDB.msi', 'SqlLocalDB.msi', '', nil);
        except
          Result := 'Failed to download SQL Server LocalDB 2022. Please verify internet connectivity.';
          Exit;
        end;
      end;
    end;

    if Exec('msiexec.exe', '/i "' + PayloadPath + '" IACCEPTSQLLOCALDBLICENSETERMS=YES /qn', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if (ResultCode <> 0) and (ResultCode <> 3010) then
        Log(Format('SqlLocalDB MSI installer returned code %d', [ResultCode]));
    end
    else
    begin
      Result := 'Failed to execute SQL Server LocalDB installer.';
      Exit;
    end;
  end;

  // 4. Configure & Start SQL Server LocalDB Instance (MSSQLLocalDB)
  WizardForm.StatusLabel.Caption := 'Configuring SQL Server LocalDB instance...';
  Exec('cmd.exe', '/c "sqllocaldb create MSSQLLocalDB & sqllocaldb start MSSQLLocalDB"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
