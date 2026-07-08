# Third-Party Notices

Clyppr is distributed with the following third-party software. Clyppr itself is
licensed under the MIT License (see `LICENSE`).

## FFmpeg

Clyppr bundles the `ffmpeg.exe` and `ffprobe.exe` command-line tools to encode
and inspect video. They are invoked as separate processes and are **not** linked
into Clyppr; they are aggregated with it for convenience.

- **Build:** GyanD/codexffmpeg 7.1 "essentials" (Windows x64 static)
- **Download:** https://github.com/GyanD/codexffmpeg/releases/tag/7.1
- **Project:** https://ffmpeg.org
- **License:** GNU General Public License, version 3 (this build includes
  GPL-licensed components). The full license text ships next to the binaries as
  `ffmpeg-LICENSE.txt`, and is also available at https://www.ffmpeg.org/legal.html
- **Source code:** available from the FFmpeg project (https://ffmpeg.org/download.html)
  and the build's release page linked above.

The FFmpeg binaries are reproduced by `scripts/fetch-ffmpeg.ps1`, which pins the
exact release URL and its SHA-256 checksum.

## .NET NuGet packages

Clyppr uses the following packages under their respective licenses (MIT unless noted):

- CommunityToolkit.Mvvm (MIT)
- NAudio (MIT)
- Vortice.* / SharpGen.Runtime (MIT) — Direct3D / DXGI interop
- Microsoft.Data.Sqlite / SQLitePCLRaw (MIT / Apache-2.0)
