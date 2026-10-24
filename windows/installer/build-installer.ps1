# Baut Lu-Mail und den Installer für eine Architektur.
# Aufruf:  pwsh windows/installer/build-installer.ps1 -Arch x64 -Version 0.2.0
# Benötigt: .NET 8 SDK und Inno Setup 6 (iscc.exe, z. B. per `winget install JRSoftware.InnoSetup`).
param(
    [ValidateSet("x64", "arm64")] [string] $Arch = "x64",
    [string] $Version = "0.1.0"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$platform = if ($Arch -eq "arm64") { "ARM64" } else { "x64" }
$publishDir = Join-Path $root "publish\win-$Arch"
$distDir = Join-Path $root "dist"

dotnet publish (Join-Path $root "windows\src\LuMail.App\LuMail.App.csproj") `
    -c Release -p:Platform=$platform -r "win-$Arch" -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw "Inno Setup 6 (iscc.exe) nicht gefunden" }

& $iscc "/DAppVersion=$Version" "/DArch=$Arch" "/DSourceDir=$publishDir" "/DOutputDir=$distDir" `
    (Join-Path $PSScriptRoot "LuMail.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup fehlgeschlagen" }
Write-Host "Fertig: dist\Lu-Mail-$Version-Windows-$Arch-Setup.exe"
