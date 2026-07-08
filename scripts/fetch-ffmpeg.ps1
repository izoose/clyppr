<#
  Downloads a pinned static ffmpeg build (with NVENC) and extracts ffmpeg.exe +
  ffprobe.exe into build/ffmpeg/. These binaries are bundled next to Clyppr.exe by
  the installer. They are intentionally NOT committed (too large) — this script
  reproduces them on demand, locally and in CI.

  Source: GyanD/codexffmpeg 7.1 "essentials" (GPL). See THIRD-PARTY-NOTICES.md.
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\build\ffmpeg")
)
$ErrorActionPreference = "Stop"

$Url    = "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip"
$Sha256 = "FA7D4D7E795DB0E2503F49F105F46ED5852386F0CFDD819899BE3B65EBDE24FC"

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$ffmpeg  = Join-Path $OutDir "ffmpeg.exe"
$ffprobe = Join-Path $OutDir "ffprobe.exe"

if ((Test-Path $ffmpeg) -and (Test-Path $ffprobe)) {
    Write-Host "ffmpeg already present in $OutDir - skipping download."
    return
}

$zip = Join-Path $OutDir "ffmpeg.zip"
Write-Host "Downloading ffmpeg from $Url ..."
Invoke-WebRequest -Uri $Url -OutFile $zip -UseBasicParsing

$actual = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash
if ($actual -ne $Sha256) {
    Remove-Item $zip -Force
    throw "SHA-256 mismatch for ffmpeg download.`n  expected $Sha256`n  actual   $actual"
}
Write-Host "SHA-256 verified."

$tmp = Join-Path $OutDir "_extract"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $tmp -Force

Copy-Item (Get-ChildItem $tmp -Recurse -Filter "ffmpeg.exe"  | Select-Object -First 1).FullName $ffmpeg  -Force
Copy-Item (Get-ChildItem $tmp -Recurse -Filter "ffprobe.exe" | Select-Object -First 1).FullName $ffprobe -Force
$lic = Get-ChildItem $tmp -Recurse -Filter "LICENSE" | Select-Object -First 1
if ($lic) { Copy-Item $lic.FullName (Join-Path $OutDir "ffmpeg-LICENSE.txt") -Force }

Remove-Item $tmp -Recurse -Force
Remove-Item $zip -Force
Write-Host "ffmpeg + ffprobe ready in $OutDir"
