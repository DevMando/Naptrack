<p align="center">

```
███╗   ██╗ █████╗ ██████╗ ████████╗██████╗  █████╗  ██████╗██╗  ██╗
████╗  ██║██╔══██╗██╔══██╗╚══██╔══╝██╔══██╗██╔══██╗██╔════╝██║ ██╔╝
██╔██╗ ██║███████║██████╔╝   ██║   ██████╔╝███████║██║     █████╔╝
██║╚██╗██║██╔══██║██╔═══╝    ██║   ██╔══██╗██╔══██║██║     ██╔═██╗
██║ ╚████║██║  ██║██║        ██║   ██║  ██║██║  ██║╚██████╗██║  ██╗
╚═╝  ╚═══╝╚═╝  ╚═╝╚═╝        ╚═╝   ╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝╚═╝  ╚═╝
```

[![NuGet](https://img.shields.io/nuget/v/Naptrack?style=flat-square&logo=nuget&color=blue)](https://www.nuget.org/packages/Naptrack)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey?style=flat-square)](https://github.com/DevMando/Naptrack)
[![Made with](https://img.shields.io/badge/Made%20with%20%3C3%20by-Mando-red?style=flat-square)](https://github.com/DevMando)

</p>

Grab audio and video from the web. Built originally to get lofi tracks offline for coding sessions.

Paste a link, pick MP3 or MP4, hit Download. That's it.

<p align="center">
  <img src="assets/Naptrack.gif" alt="Naptrack Demo" width="700" />
</p>

Built with .NET 10 and [RazorConsole](https://github.com/AaronJMcilvaine/razorconsole).

## Features

- Download audio (MP3) or video (MP4) from YouTube, TikTok, Instagram, Facebook, and [1000+ sites](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md)
- MP3/MP4 format toggle
- Auto-downloads `yt-dlp` and `ffmpeg` on first run (no manual setup)
- Keeps `yt-dlp` up to date automatically, so sites do not start refusing downloads as it ages
- Native folder picker to change download location
- Cross-platform: Windows, Linux, macOS
- Keyboard-driven TUI with animated spinners

## Installation

### Option 1: .NET Tool (requires .NET 10 SDK)

```bash
dotnet tool install -g Naptrack
```

Then run from anywhere:

```bash
naptrack
```

### Option 2: Standalone Binary (no dependencies)

Download the latest release for your platform from [Releases](https://github.com/DevMando/Naptrack/releases). No .NET installation needed.

Or build it yourself:

```bash
# Windows
dotnet publish -r win-x64

# Linux
dotnet publish -r linux-x64

# macOS Intel
dotnet publish -r osx-x64

# macOS Apple Silicon
dotnet publish -r osx-arm64
```

The output is a single executable in `bin/Release/net10.0/<runtime>/publish/`.

## Usage

1. Launch `naptrack`
2. Paste a URL (YouTube, TikTok, Instagram, Facebook, etc.)
3. Select **MP3** or **MP4**
4. Press **Enter** or Tab to **[ Download ]** and press Enter
5. File downloads to your configured folder

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| **Tab** | Navigate between elements |
| **Enter** | Select / confirm |
| **Esc** | Clear input and errors |
| **Ctrl+V** | Paste (Windows) |
| **Cmd+V** | Paste (macOS) |
| **Ctrl+Shift+V** | Paste (Linux) |
| **Ctrl+C** | Quit |

## Configuration

- **Download folder**: Click the folder path or use **[ Change ]** to open a folder picker
- The URL box empties as soon as a download is accepted, so it is ready for the next link. The
  link is not lost — press **↑** to bring it back from history if a download needs another go.
- Settings are saved to:
  - Windows: `%APPDATA%\Naptrack\config.json`
  - Linux: `~/.config/Naptrack/config.json`
  - macOS: `~/Library/Application Support/Naptrack/config.json`

## Dependencies

Naptrack automatically downloads these on first run if not found:

- [yt-dlp](https://github.com/yt-dlp/yt-dlp) — media extraction from 1000+ sites
- [ffmpeg](https://ffmpeg.org/) — audio/video format conversion

They live alongside your settings, in `bin/` next to `config.json`.

### Keeping yt-dlp current

yt-dlp is the part of Naptrack that goes out of date. Sites change what they accept on no
schedule, and an old build fails in a way that looks like being blocked: the video is found and
its formats are listed, then the download itself comes back `HTTP Error 403`.

So Naptrack tracks yt-dlp's [nightly channel](https://github.com/yt-dlp/yt-dlp-nightly-builds) —
the same code that becomes the next stable release, published as it lands — and checks for a
newer build at most once a day, updating in the background while it starts. If you are offline,
rate limited, or already current, nothing happens and nothing is said.

The version in use is shown in the app. It turns yellow once the build is more than two weeks
old, and **[ Update yt-dlp ]** next to it fetches the newest build immediately. That button is
what the "Access denied" message is pointing you at.

If you already have yt-dlp installed through pip, Homebrew or a package manager, Naptrack uses it
and leaves it alone — updating someone else's install is not its business. The exception is a
system copy that has gone stale: rather than leave you with downloads that cannot succeed,
Naptrack installs its own up-to-date copy and uses that instead. Yours is left untouched.

### JavaScript runtime (optional)

YouTube's player challenges are JavaScript, and yt-dlp now warns that extracting without an
engine to run them is deprecated and may drop some formats. yt-dlp finds
[Deno](https://deno.com/) by itself; Naptrack additionally detects
[Node](https://nodejs.org/) or [Bun](https://bun.sh/) and points yt-dlp at whichever it finds.
None of them are required, and installing one is not a fix for being blocked — it affects which
formats are available, not whether the download is allowed.

## License

MIT
