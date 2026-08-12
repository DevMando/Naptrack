using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Naptrack.Services;

public class BinaryDownloader
{
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

        try
        {
            var bytes = await Http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(YtDlpPath, bytes, ct);
            MakeExecutable(YtDlpPath);
            onStatus?.Invoke("yt-dlp downloaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            onStatus?.Invoke($"Failed to download yt-dlp: {ex.Message}");
            return false;
        }
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
        const string baseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download";

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
