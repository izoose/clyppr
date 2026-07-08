# Clyppr — Installer, GitHub, and Website Release Design

**Date:** 2026-07-08
**Status:** Approved
**Repo:** https://github.com/izoose/clyppr.git (public)

## Goal

Turn the working "Clipper" WPF app into a shippable product named **Clyppr**:
a proper Windows installer, a GitHub-ready public repo with CI/release automation,
and a live marketing site at **clyppr.com** whose Download button installs the app.

## Decisions (locked)

| Area | Choice |
|------|--------|
| Installer tech | **Inno Setup** (`.exe` wizard), per-user, no UAC |
| ffmpeg | **Bundled** in the installer (pinned static build w/ NVENC) |
| Website hosting | **GitHub Pages** + custom domain `clyppr.com` (CNAME) |
| Naming | **Full rebrand to Clyppr** — output exe `Clyppr.exe`, titles, run-key |
| License | **MIT** (app source); ffmpeg bundled as separate GPL program (aggregation) |
| Release | I push to `izoose/clyppr`, tag `v1.0.0`, `gh release create` with installer |

## 1. Rebrand

- `Clipper.App.csproj`: `<AssemblyName>Clyppr</AssemblyName>` + `Product`/`Version`/`Company`/`Description` metadata. Output exe = `Clyppr.exe`.
- `app.manifest`: `assemblyIdentity name="Clyppr"`.
- Audit user-visible `Clipper` strings (window titles, tray tooltip, notifications) → `Clyppr`.
- **Namespaces stay `Clipper.*`** (internal, never shown; renaming is risky churn — YAGNI).
- `StartupManager` already writes a `Clyppr` HKCU Run value and removes the legacy `Clipper` one — no change needed.

## 2. Installer — `installer/clyppr.iss`

- `PrivilegesRequired=lowest` → installs to `%LocalAppData%\Programs\Clyppr`, no UAC.
- App requires no admin at runtime (hotkeys + DXGI capture work as normal user).
- Files: `Clyppr.exe`, `ffmpeg.exe`, `ffprobe.exe`, `LICENSE`, `THIRD-PARTY-NOTICES.md`.
  ffmpeg found via `CreateProcess` search order (app dir searched first) — no code change.
- Start Menu shortcut; optional desktop shortcut (task).
- Task "Start Clyppr when I sign in to Windows" → writes HKCU `...\Run\Clyppr` = `"<dir>\Clyppr.exe" --tray` (matches `StartupManager` format).
- "Launch Clyppr" checkbox on finish page.
- Uninstall removes app + shortcuts + run-key; **preserves user clip library/settings**.
- Stable AppId GUID. `MinVersion` = Windows 10. LZMA2/max, solid compression.
- Output filename: **`Clyppr-Setup-x64.exe`** (no version → permanent website link). Unsigned.

## 3. ffmpeg bundling

- `scripts/fetch-ffmpeg.ps1`: download pinned `GyanD/codexffmpeg` 7.1 essentials zip
  (includes `h264_nvenc`), verify SHA-256, extract `ffmpeg.exe`+`ffprobe.exe` → `build/ffmpeg/`.
- `build/` and `dist/` gitignored (binaries too large).
- `THIRD-PARTY-NOTICES.md`: ffmpeg GPL attribution + source URL.

## 4. Build pipeline — `scripts/package.ps1`

1. `dotnet publish` self-contained single-file → `Clyppr.exe`.
2. Copy `ffmpeg.exe`/`ffprobe.exe` alongside it.
3. `ISCC installer/clyppr.iss` → `dist/Clyppr-Setup-x64.exe`.

`build-release.ps1` (dev single-exe) kept for quick local runs.

## 5. GitHub-ready

- **`.gitignore`**: un-ignore `site/img/**` (current `*.png` rule hides screenshots);
  add `build/`, `dist/`, root `ChatGPT Image *.png`.
- **`LICENSE`**: MIT.
- **`README.md`**: rewrite for Clyppr — download, build-from-source, features, screenshots;
  remove stale VPS/`server/` section.
- **`.github/workflows/release.yml`**: on `v*` tag, `windows-latest`, install Inno Setup,
  run `scripts/package.ps1`, upload `Clyppr-Setup-x64.exe` to the release.
- **`.github/workflows/pages.yml`**: on push to `main` touching `site/**`, deploy `site/` to Pages.
- Rename branch `master` → `main`; commit all current work; `git remote add origin`; push.

## 6. Website — `site/`

- Wire all Download buttons → `https://github.com/izoose/clyppr/releases/latest/download/Clyppr-Setup-x64.exe`; remove dead `onclick`.
- `site/CNAME` = `clyppr.com`.
- Add canonical / `og:url` / `og:image` (`https://clyppr.com/img/app-home.png`).
- Correct the "~40 MB" size note to the real installer size.
- Provide DNS records: 4 apex `A` records (185.199.108–111.153) + `www` CNAME → `izoose.github.io`.

## 7. Execution order

Install Inno Setup → rebrand → installer script + scripts → repo files → workflows → site →
fetch ffmpeg → **build installer + verify `Clyppr.exe` launches & finds ffmpeg** →
commit → rename branch → remote add → push → tag `v1.0.0` → `gh release create` → enable Pages → DNS instructions.

## Verification

- Installer compiles to `dist/Clyppr-Setup-x64.exe`.
- Installed `Clyppr.exe` launches; `ffmpeg`/`ffprobe` resolve from the install dir.
- Release page shows the installer asset; website latest-download URL resolves to it.

## Out of scope

- Code signing (unsigned; SmartScreen "Run anyway" noted).
- Auto-update (chose Inno over Velopack).
- Renaming internal `Clipper.*` namespaces/assemblies.
