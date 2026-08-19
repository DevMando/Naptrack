# Changelog

## 1.0.4 (2026-08-18)

### Fixes
- Downloads that failed with "Access denied" or what looked like being blocked, most visibly on YouTube. yt-dlp was downloaded once on first run and then never touched again, so every install eventually drifted far enough behind the sites it talks to that extraction still succeeded but the media request came back `HTTP Error 403`. Nothing inside the app could fix it. yt-dlp is now kept current.
- yt-dlp now comes from the nightly channel rather than stable. Sites change what they accept on no schedule and stable ships every few weeks; every gap between the two was a window where downloads failed for reasons no amount of retrying could clear.
- A failed or interrupted yt-dlp download no longer leaves a truncated binary where the working one was. The new copy is staged to a temporary file and moved into place only once it has fully arrived.
- The error shown for a blocked request now points at the update button instead of implying the site made a permission decision. A sign-in check is no longer retried, because retrying it never worked.

### Interface
- The URL box now empties as soon as a download starts, ready for the next link. It used to hold the text until the download succeeded. The link still goes to the top of the history, so **↑** brings it straight back if it needs another go.
- Esc, **↑** and **↓** had no effect on the URL box. All three changed the value behind the scenes but never asked for a repaint, and the input only re-reads its value when it is handed one — so the box kept showing the old text no matter what you pressed.
- The controls between the URL box and the downloads list are no longer centred one cluster at a time. Format, playlist, folder and yt-dlp now share a single left-aligned label column, with the download choices and the folder/tooling rows separated into two groups.
- The playlist choice resets when the box is cleared, instead of silently carrying over to the next playlist link.
- Removed the **[ Quit ]** button; Ctrl+C quits. It sat next to **[ Change ]**, which put an exit one keystroke away from a settings control.

### Features
- Naptrack checks for a newer yt-dlp at most once a day, and updates it in the background during startup. Offline, rate limited, or already current, it stays silent.
- The yt-dlp version is shown in the app, and turns yellow once the build is more than two weeks old. There is an `[ Update yt-dlp ]` button next to it that fetches the newest build on demand, ignoring the daily check.
- A yt-dlp already installed on your system is used as before and left alone — except when it has gone stale, in which case Naptrack installs its own up-to-date copy rather than leaving you with downloads that cannot work.
- YouTube's player challenges are JavaScript, and yt-dlp now warns that extracting without an engine to run them is deprecated and drops some formats. Naptrack detects Node or Bun on your system and points yt-dlp at it. Deno is found by yt-dlp on its own.

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
