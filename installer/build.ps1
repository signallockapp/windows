# SignalLock installer build script.
# Runs `dotnet publish` then invokes Inno Setup to produce a setup .exe.
# Output: windows\installer\output\SignalLock-Setup-<version>-x64.exe
#
# Prerequisites:
#   - .NET 8 SDK              https://dotnet.microsoft.com/download/dotnet/8.0
#   - Inno Setup 6            https://jrsoftware.org/isdl.php
#
# Usage (from repo root, in PowerShell on Windows):
#   .\windows\installer\build.ps1
#   .\windows\installer\build.ps1 -Runtime win-arm64
#   .\windows\installer\build.ps1 -Configuration Debug    # not signed, larger

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$installerDir = $PSScriptRoot
$windowsDir   = Resolve-Path "$installerDir\.."
$csprojPath   = Join-Path $windowsDir "SignalLock.csproj"
$publishDir   = Join-Path $windowsDir "publish"
$issPath      = Join-Path $installerDir "SignalLock.iss"

# 1) Read version from csproj — single source of truth.
[xml]$csproj = Get-Content $csprojPath
$versionNode = $csproj.SelectSingleNode("//Project/PropertyGroup/Version")
if (-not $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "Version not found in $csprojPath"
}
$version = $versionNode.InnerText.Trim()
Write-Host "==> SignalLock $version ($Runtime, $Configuration)" -ForegroundColor Cyan

# 2) Clean previous publish output, then publish.
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
Write-Host "==> dotnet publish" -ForegroundColor Cyan
dotnet publish $csprojPath -c $Configuration -r $Runtime -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exePath = Join-Path $publishDir "SignalLock.exe"
if (-not (Test-Path $exePath)) { throw "Expected $exePath was not produced" }
$exeSizeMB = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host "    published: $exePath ($exeSizeMB MB)"

# 3) Locate ISCC.exe.
$isccCandidates = @(
    "${Env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${Env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ }

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isdl.php"
}
Write-Host "==> $iscc" -ForegroundColor Cyan

# 4) Compile installer with the version injected.
& $iscc "/DMyAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$setupExe = Join-Path $installerDir "output\SignalLock-Setup-$version-x64.exe"
if (Test-Path $setupExe) {
    $setupSizeMB = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "OK  $setupExe ($setupSizeMB MB)" -ForegroundColor Green
} else {
    Write-Host "OK  installer compiled — see $installerDir\output\" -ForegroundColor Green
}
