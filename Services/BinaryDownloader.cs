using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Naptrack.Services;

public class BinaryDownloader
{
    /// <summary>
    /// Nightly builds rather than stable releases.
    ///
    /// YouTube changes what it accepts on no schedule, and stable yt-dlp ships every few weeks.
    /// Every gap between the two is a window where downloads fail with "HTTP Error 403" on the
    /// media request even though extraction succeeded -- the exact symptom that looks to a user
    /// like being blocked. The nightly channel closes that window: it is the same code that
    /// becomes the next stable, published as it lands, and it is what yt-dlp's own maintainers
    /// point at when a site breaks mid-cycle.
    /// </summary>
    private const string YtDlpRepo = "yt-dlp/yt-dlp-nightly-builds";

    private const string LatestReleaseApiUrl = $"https://api.github.com/repos/{YtDlpRepo}/releases/latest";

    private static readonly string BinDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Naptrack", "bin");

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    static BinaryDownloader()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Naptrack/1.0");
    }

    public string BinDirectory => BinDir;

    public string YtDlpPath => Path.Combine(BinDir, GetYtDlpFilename());
    public string FfmpegPath => Path.Combine(BinDir, GetFfmpegFilename());

    public bool YtDlpExists => File.Exists(YtDlpPath);
    public bool FfmpegExists => File.Exists(FfmpegPath);

    public async Task<bool> DownloadYtDlpAsync(Action<string>? onStatus = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(BinDir);
        var url = GetYtDlpDownloadUrl();
        onStatus?.Invoke("Downloading yt-dlp...");

        // Staged through a temp file and moved into place, rather than written straight to
        // YtDlpPath. A refresh replaces a binary that already works, and a download that dies
        // half way through would otherwise leave a truncated exe where the working one was --
        // turning "your yt-dlp is a few weeks old" into "Naptrack no longer runs at all".
        var staging = YtDlpPath + ".download";

        try
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(staging);
                await stream.CopyToAsync(file, ct);
            }

            File.Move(staging, YtDlpPath, overwrite: true);
            MakeExecutable(YtDlpPath);
            onStatus?.Invoke("yt-dlp downloaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            // Windows refuses to replace a file that is still mapped by a running process, so a
            // refresh attempted while a download is in flight lands here. The existing binary is
            // untouched and the next launch retries, which is the right outcome either way.
            TryDelete(staging);
            onStatus?.Invoke($"Failed to download yt-dlp: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Tag of the most recent published yt-dlp build, or null when GitHub cannot be reached or
    /// answers in a shape this does not recognise. Costs one small request, which is what makes
    /// it worth doing before committing to a ~17MB binary download.
    /// </summary>
    public async Task<string?> FetchLatestYtDlpVersionAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApiUrl, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return document.RootElement.TryGetProperty("tag_name", out var tag)
                ? tag.GetString()
                : null;
        }
        catch
        {
            // Offline, rate limited, or the response changed shape. The caller treats an unknown
            // remote version as "nothing to compare against" and leaves the local binary alone.
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best effort */ }
    }

    public async Task<bool> DownloadFfmpegAsync(Action<string>? onStatus = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(BinDir);
        onStatus?.Invoke("Downloading ffmpeg (this may take a minute)...");

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await DownloadFfmpegWindowsAsync(ct);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                await DownloadFfmpegMacAsync(ct);
            }
            else
            {
                await DownloadFfmpegLinuxAsync(ct);
            }

            onStatus?.Invoke("ffmpeg downloaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            onStatus?.Invoke($"Failed to download ffmpeg: {ex.Message}");
            return false;
        }
    }

    private static string GetYtDlpFilename()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "yt-dlp.exe";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "yt-dlp";
        return "yt-dlp";
    }

    private static string GetFfmpegFilename()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
    }

    private static string GetYtDlpDownloadUrl()
    {
        const string baseUrl = $"https://github.com/{YtDlpRepo}/releases/latest/download";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"{baseUrl}/yt-dlp.exe";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"{baseUrl}/yt-dlp_macos";

        var arm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        // The plain linux builds are linked against glibc and will not start on
        // Alpine and friends, which need the musllinux builds instead.
        if (IsMuslLinux())
            return arm64 ? $"{baseUrl}/yt-dlp_musllinux_aarch64" : $"{baseUrl}/yt-dlp_musllinux";

        return arm64 ? $"{baseUrl}/yt-dlp_linux_aarch64" : $"{baseUrl}/yt-dlp_linux";
    }

    private static bool IsMuslLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        if (RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            // A musl system ships its loader as /lib/ld-musl-<arch>.so.1.
            return Directory.EnumerateFiles("/lib", "ld-musl-*.so.1").Any();
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadFfmpegWindowsAsync(CancellationToken ct)
    {
        const string url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip";
        var zipPath = Path.Combine(BinDir, "ffmpeg.zip");

        await DownloadFileAsync(url, zipPath, ct);
        ExtractFfmpegBinFolderFromZip(zipPath);
        File.Delete(zipPath);
    }

    private async Task DownloadFfmpegLinuxAsync(CancellationToken ct)
    {
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linuxarm64" : "linux64";
        var url = $"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-{arch}-gpl.tar.xz";
        var archivePath = Path.Combine(BinDir, "ffmpeg.tar.xz");

        await DownloadFileAsync(url, archivePath, ct);
        await ExtractFfmpegFromTarXzAsync(archivePath);
        File.Delete(archivePath);
    }

    private async Task DownloadFfmpegMacAsync(CancellationToken ct)
    {
        // evermeet publishes x86_64 only, so Apple Silicon would be stuck on Rosetta.
        // These are bare arm64 binaries rather than an archive, so there is nothing to unpack.
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            const string armUrl =
                "https://github.com/eugeneware/ffmpeg-static/releases/latest/download/ffmpeg-darwin-arm64";

            await DownloadFileAsync(armUrl, FfmpegPath, ct);
            MakeExecutable(FfmpegPath);
            return;
        }

        const string url = "https://evermeet.cx/ffmpeg/get/zip";
        var zipPath = Path.Combine(BinDir, "ffmpeg.zip");

        await DownloadFileAsync(url, zipPath, ct);
        ExtractFfmpegFromZip(zipPath, "ffmpeg");
        File.Delete(zipPath);
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destPath);
        await stream.CopyToAsync(file, ct);
    }

    // The win64 "shared" build links ffmpeg.exe against the av*/sw* DLLs that sit
    // beside it in bin/, so pulling out the exe on its own leaves it unable to start.
    // Everything in bin/ has to land in BinDir together.
    private void ExtractFfmpegBinFolderFromZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var entries = archive.Entries
            .Where(e => e.Name.Length > 0)
            .Where(e => e.FullName.Contains("bin/", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                     || e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(e => !e.Name.Equals("ffplay.exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var ffmpegName = GetFfmpegFilename();
        if (!entries.Any(e => e.Name.Equals(ffmpegName, StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException($"Could not find {ffmpegName} in archive.");

        foreach (var entry in entries)
        {
            // Flatten to entry.Name so a crafted archive cannot escape BinDir.
            var destination = Path.Combine(BinDir, entry.Name);
            entry.ExtractToFile(destination, overwrite: true);
        }

        MakeExecutable(FfmpegPath);
    }

    private void ExtractFfmpegFromZip(string zipPath, string targetName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) &&
            e.FullName.Contains("bin", StringComparison.OrdinalIgnoreCase));

        // Fall back to any entry matching the filename
        entry ??= archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            throw new FileNotFoundException($"Could not find {targetName} in archive.");

        entry.ExtractToFile(FfmpegPath, overwrite: true);
        MakeExecutable(FfmpegPath);
    }

    private async Task ExtractFfmpegFromTarXzAsync(string archivePath)
    {
        // Use tar command to extract just the ffmpeg binary
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"xf \"{archivePath}\" --wildcards --no-anchored \"ffmpeg\" --strip-components=2 -C \"{BinDir}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start tar.");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            // Try without --wildcards (some tar versions don't support it)
            var psi2 = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"xf \"{archivePath}\" -C \"{BinDir}\" --strip-components=2",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process2 = System.Diagnostics.Process.Start(psi2)
                ?? throw new InvalidOperationException("Failed to start tar.");
            await process2.WaitForExitAsync();
        }

        MakeExecutable(FfmpegPath);
    }

    private static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            System.Diagnostics.Process.Start("chmod", $"+x \"{path}\"")?.WaitForExit();
        }
        catch
        {
            // Best effort
        }
    }
}
