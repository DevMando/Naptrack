# Changelog

## 1.0.2 (2026-08-11)

### Fixes
- Standalone binaries were unusable on every platform, showing only "Page not found. Press Tab to navigate." IL trimming removed the Razor routes, which the router locates by reflection. Trimming is now off. If you downloaded a binary from Releases for 1.0.0 or 1.0.1, replace it.
- ffmpeg setup on Windows failed because only `ffmpeg.exe` was pulled out of an archive whose build depends on the DLLs sitting beside it. The whole `bin` folder is now extracted.
- The dependency check could hang indefinitely on Windows. `ffmpeg -version` writes more output than the pipe buffer holds, which deadlocked the probe. Both pipes are now drained together, with a timeout as a backstop.
- macOS on Apple Silicon installed an x86_64 ffmpeg and ran it through Rosetta. A native arm64 build is used instead.
- Alpine and other musl-based systems installed a glibc-linked yt-dlp that could not start. musl is now detected and the matching build selected.

### Features
- Naptrack checks nuget.org at most once a day and shows a banner when a newer version is available. The check is cached, never blocks startup, and stays quiet if you are offline.

## 1.0.1 (2026-04-09)

### Fixes
- Fix ASCII banner misalignment caused by multi-line Markup inside a single Align component

## 1.0.0 (2026-04-09)

Initial release.

### Features
- Download audio (MP3) or video (MP4) from YouTube, TikTok, Instagram, Facebook, and 1000+ sites
- MP3/MP4 format toggle
- Auto-downloads yt-dlp and ffmpeg on first run — no manual setup required
- Native folder picker (Windows, Linux, macOS)
- Configurable download folder with persistent settings
- Keyboard-driven TUI with animated spinners
- Cross-platform: Windows, Linux, macOS
- Available as a .NET global tool or standalone single-file binary
