# Builds a self-contained single-file release of Clipper into ./publish
$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "publish"

dotnet publish (Join-Path $PSScriptRoot "src/Clipper.App/Clipper.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $out

Write-Host ""
Write-Host "Published to $out\Clyppr.exe"
Write-Host "Copy ffmpeg.exe and ffprobe.exe next to Clyppr.exe (or keep them on PATH)."
Write-Host "For the full installer instead, run: scripts\package.ps1"
