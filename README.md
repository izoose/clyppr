# Clipper

A simpler, self-hosted alternative to Medal for game clipping. Records gameplay with
**per-application audio on separate tracks** (so you can cut Discord voice but keep
game audio), trims clips in a clean editor, and copies a shareable link hosted on your
own VPS.

Built in C# / .NET 8 (WPF UI) with a custom capture engine: Windows Graphics Capture
for video, WASAPI process-loopback for per-app audio, NVENC for encoding.

## Status

Early. `spikes/` contains throwaway **M0 viability spikes** that prove the two hard parts
work before the real app is built:

- `spikes/S1.VideoCapture` — WGC screen capture encoded to H.264 via NVENC.
- `spikes/S2.AudioSplit` — WASAPI process-loopback proving per-app audio separation.

See `docs/superpowers/plans/` for the plan driving this.
