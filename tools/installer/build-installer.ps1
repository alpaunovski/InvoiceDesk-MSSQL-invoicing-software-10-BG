# PowerShell script to publish InvoiceDesk and compile the Inno Setup installer
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $false
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ScriptDir "..\..\InvoiceDesk\InvoiceDesk.csproj"
$IssFile = Join-Path $ScriptDir "InvoiceDesk.iss"

Write-Host "=== 1. Publishing InvoiceDesk WPF Application ===" -ForegroundColor Cyan
$selfContainedFlag = if ($SelfContained) { "true" } else { "false" }

dotnet publish $ProjectFile -c $Configuration -r $Runtime --self-contained $selfContainedFlag /p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed."
    exit $LASTEXITCODE
}

Write-Host "`n=== 2. Locating Inno Setup Compiler (ISCC.exe) ===" -ForegroundColor Cyan
$isccPaths = @(
    "ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$isccPath = $null
foreach ($path in $isccPaths) {
    if (Get-Command $path -ErrorAction SilentlyContinue) {
        $isccPath = $path
        break
    }
}

if (-not $isccPath) {
    Write-Warning "Inno Setup Compiler (ISCC.exe) was not found in PATH or standard Program Files locations."
    Write-Host "Publish complete! To build the installer package, open '$IssFile' in Inno Setup Compiler." -ForegroundColor Green
    exit 0
}

Write-Host "`n=== 3. Compiling Inno Setup Installer ===" -ForegroundColor Cyan
& $isccPath $IssFile

if ($LASTEXITCODE -eq 0) {
    $outputExe = Join-Path $ScriptDir "Output\InvoiceDesk-setup.exe"
    Write-Host "`nInstaller created successfully: $outputExe" -ForegroundColor Green
} else {
    Write-Error "Inno Setup compilation failed."
}
