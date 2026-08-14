# Changelog

## 1.0.3 (2026-08-14)

### Features
- Downloads are now a queue rather than one at a time. Paste a link, hit Enter, and paste the next one while the first is still going. Each row shows its own progress bar, transfer speed, ETA and Cancel button. Up to three run at once; the rest wait as "Queued".
- Completed downloads name the file they saved and where it went, and stay listed instead of being replaced by the next one.
- The URL box remembers what you have submitted. Press up and down to walk back through previous links.
- Transient failures retry automatically, up to three attempts with a visible countdown. Only failures that could plausibly clear are retried — a private, deleted or age-restricted video fails immediately rather than making you wait.
- Your format choice, download folder and recent downloads are remembered between sessions.

### Fixes
- A playlist link no longer downloads the entire playlist by default. Links carrying a `list=` parameter — which is most links copied from YouTube — now download just the video you asked for, with an explicit option to take the whole playlist. When you do, the row shows its position in the playlist.
- Fixed a crash that could take the app down while navigating with Tab during a download.
- Progress sat at 0% for the whole download on systems using a comma as the decimal separator. Percentages are now parsed independently of locale.
- MP4 downloads reported "Merging" while the audio stream was still downloading, and never recovered.
- Cancelling a download while another was running could leave the row frozen with an unresponsive Cancel button.
- Non-Latin filenames no longer break the alignment of the downloads list.
- Rows now use the full width of the terminal and adapt when it is resized.
- The download folder confirmation no longer stays on screen for the rest of the session.

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
