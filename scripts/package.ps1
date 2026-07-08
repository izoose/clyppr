<#
  Builds the full Clyppr release installer.

    1. Publishes a self-contained single-file Clyppr.exe (no .NET install needed).
    2. Fetches + bundles ffmpeg.exe / ffprobe.exe (NVENC static build).
    3. Compiles installer\clyppr.iss with Inno Setup -> dist\Clyppr-Setup-x64.exe.

  Usage:  pwsh scripts\package.ps1 [-Version 1.0.0]
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
$root  = Split-Path $PSScriptRoot -Parent
$dist  = Join-Path $root "dist"
$stage = Join-Path $dist "app"

Write-Host "== Clyppr release build $Version ==" -ForegroundColor Cyan

# --- 1. publish the app -------------------------------------------------------
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

dotnet publish (Join-Path $root "src\Clipper.App\Clipper.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (-not (Test-Path (Join-Path $stage "Clyppr.exe"))) {
    throw "Expected Clyppr.exe in $stage - check AssemblyName in the csproj."
}

# --- 2. bundle ffmpeg ---------------------------------------------------------
& (Join-Path $PSScriptRoot "fetch-ffmpeg.ps1")
$ff = Join-Path $root "build\ffmpeg"
Copy-Item (Join-Path $ff "ffmpeg.exe")  $stage -Force
Copy-Item (Join-Path $ff "ffprobe.exe") $stage -Force
if (Test-Path (Join-Path $ff "ffmpeg-LICENSE.txt")) {
    Copy-Item (Join-Path $ff "ffmpeg-LICENSE.txt") $stage -Force
}

# license / notices shipped alongside the binaries
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt") -Force
if (Test-Path (Join-Path $root "THIRD-PARTY-NOTICES.md")) {
    Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $stage -Force
}

# --- 3. compile the installer -------------------------------------------------
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) not found. Install: winget install JRSoftware.InnoSetup" }

& $iscc `
    "/DAppVersion=$Version" `
    "/DSourceDir=$stage" `
    "/DOutputDir=$dist" `
    (Join-Path $root "installer\clyppr.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

$setup = Join-Path $dist "Clyppr-Setup-x64.exe"
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host ""
Write-Host "Built $setup ($mb MB)" -ForegroundColor Green
