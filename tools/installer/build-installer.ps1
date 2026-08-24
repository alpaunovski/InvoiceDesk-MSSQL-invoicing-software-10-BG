<#
.SYNOPSIS
    Builds InvoiceDesk and compiles the Inno Setup installer.
.DESCRIPTION
    1. Publishes InvoiceDesk in Release mode for win-x64.
    2. (Optional) Downloads prerequisite payloads to tools/installer/Payloads for offline installer creation.
    3. Invokes ISCC (Inno Setup Compiler) to build InvoiceDesk-Setup.exe.
#>

[CmdletBinding()]
param (
    [switch]$DownloadPayloads
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = System.IO.Path::GetFullPath((Join-Path $scriptDir "..\.."))
$projectFile = Join-Path $rootDir "InvoiceDesk\InvoiceDesk.csproj"
$publishDir = Join-Path $rootDir "InvoiceDesk\bin\Release\net8.0-windows\win-x64\publish"
$issFile = Join-Path $scriptDir "InvoiceDesk.iss"
$payloadsDir = Join-Path $scriptDir "Payloads"

Write-Host "==> Publishing InvoiceDesk (Release win-x64)..." -ForegroundColor Green
dotnet publish $projectFile -c Release -r win-x64 --self-contained false -o $publishDir

if ($DownloadPayloads) {
    Write-Host "==> Downloading offline installer payloads..." -ForegroundColor Green
    if (-not (Test-Path $payloadsDir)) {
        New-Item -ItemType Directory -Path $payloadsDir | Out-Null
    }
    
    $localDbUrl = "https://download.microsoft.com/download/7/c/1/7c14e92e-bdcb-4c89-dadc-4a30e32b4952/SqlLocalDB.msi"
    $dotnetUrl  = "https://dotnetcli.azureedge.net/dotnet/Runtime/8.0.13/windowsdesktop-runtime-8.0.13-win-x64.exe"
    $wv2Url     = "https://msedge.sf.dl.delivery.mp.microsoft.com/filestream/MicrosoftEdgeWebview2Setup.exe"

    Write-Host "Downloading SQL Server LocalDB 2022..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $localDbUrl -OutFile (Join-Path $payloadsDir "SqlLocalDB.msi")
    
    Write-Host "Downloading .NET 8 Desktop Runtime..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $dotnetUrl  -OutFile (Join-Path $payloadsDir "windowsdesktop-runtime-8.0-win-x64.exe")
    
    Write-Host "Downloading Microsoft Edge WebView2..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $wv2Url     -OutFile (Join-Path $payloadsDir "MicrosoftEdgeWebview2Setup.exe")
}

$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$isccPath = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $isccPath) {
    $commandInPath = Get-Command iscc -ErrorAction SilentlyContinue
    if ($commandInPath) { $isccPath = $commandInPath.Source }
}

if ($isccPath) {
    Write-Host "==> Compiling installer with Inno Setup ($isccPath)..." -ForegroundColor Green
    & $isccPath $issFile
    Write-Host "==> Installer compiled successfully!" -ForegroundColor Green
} else {
    Write-Host "==> Inno Setup Compiler (ISCC.exe) not found on PATH or standard directories." -ForegroundColor Yellow
    Write-Host "    You can compile '$issFile' manually using Inno Setup GUI Compiler." -ForegroundColor Yellow
}
