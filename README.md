# Clipper

A simpler, self-hosted alternative to Medal for game clipping. Always-on replay buffer,
one-hotkey clips, **per-application audio on separate tracks** (cut Discord voice but keep
game/Roblox audio), a clean trim/mute editor, and **copy-a-link** sharing hosted on your
own VPS.

Built in **C# / .NET 8** (WPF) with a custom capture engine — Windows Graphics Capture
(with a DXGI Desktop Duplication fallback) for video, WASAPI process-loopback for per-app
audio, and NVENC hardware encoding via a bundled/`PATH` ffmpeg.

## Features

- **Replay buffer + hotkey** — always recording the last N seconds; press **ALT+C** (configurable) to save a clip.
- **Per-app audio tracks** — each clip is one MP4 with separate *Desktop / Voice / Mic* audio tracks.
- **Editor** — trim (Set Start/End), keep/cut each audio source, per-track volume, export.
- **Library** — Medal-style dark grid: thumbnails, search, play, rename, delete, import.
- **Sharing** — upload to your VPS, get a `https://your-domain/c/<id>` link that embeds in Discord.
- **Background** — runs in the system tray; optional run-on-startup.

## Requirements

- Windows 10 build 20348+ / Windows 11 (per-app audio needs it).
- **ffmpeg** with NVENC on `PATH` (or place `ffmpeg.exe` / `ffprobe.exe` next to `Clipper.App.exe`).
- An NVIDIA/AMD/Intel GPU with a hardware H.264 encoder (auto-detected; NVENC is the primary target).

> Note: some "debloated" gaming PCs disable the Windows `CaptureService`, which breaks the
> modern capture API — Clipper automatically falls back to DXGI Desktop Duplication, which
> works without it.

## Build & run (dev)

```bash
dotnet build Clipper.sln
dotnet run --project src/Clipper.App
```

## Release build (single exe)

```powershell
./build-release.ps1
# output: publish/Clipper.App.exe  (self-contained, no .NET install needed)
```

Put `ffmpeg.exe` and `ffprobe.exe` next to the exe (or ensure they're on `PATH`).

## Sharing server

The share backend lives in [`server/`](server/README.md) — a zero-dependency Node server you
deploy to your VPS. After deploying, open **Settings → Sharing** in the app and set the server
URL + upload token.

## Project layout

- `src/Clipper.Engine` — capture (WGC/DXGI), per-app audio (WASAPI process-loopback), recorder, replay buffer, hotkey.
- `src/Clipper.Core` — library (SQLite), settings, ffprobe/thumbnails, exporter, share client.
- `src/Clipper.App` — WPF UI (library, editor, settings, tray).
- `server/` — Node share server for the VPS.
- `spikes/`, `tools/` — throwaway M0 proof code + an engine test harness.
