using System.Diagnostics;
using System.Text.RegularExpressions;
using Naptrack.Models;

namespace Naptrack.Services;

public partial class YtDlpService
{
    private readonly DependencyChecker _depChecker;

    public YtDlpService(DependencyChecker depChecker)
    {
        _depChecker = depChecker;
    }

    [GeneratedRegex(@"(\d+\.?\d*)%")]
    private static partial Regex ProgressRegex();

    public async Task DownloadAsync(
        string url,
        string outputDir,
        DownloadFormat format,
        Action<double> onProgress,
        Action<string> onStatus,
        Action<bool, string> onComplete,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var ytDlpPath = _depChecker.YtDlpPath;
        var ffmpegDir = Path.GetDirectoryName(_depChecker.FfmpegPath);
        var ffmpegArgs = !string.IsNullOrEmpty(ffmpegDir) && ffmpegDir != "."
            ? $"--ffmpeg-location \"{ffmpegDir}\" " : "";

        var formatArgs = format == DownloadFormat.Mp3
            ? "-x --audio-format mp3 --audio-quality 0"
            : "-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]\" --merge-output-format mp4";

        var psi = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments = $"{ffmpegArgs}{formatArgs} -o \"%(title)s.%(ext)s\" --newline --progress \"{url}\"",
            WorkingDirectory = outputDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process is null)
            {
                onComplete(false, "Failed to start yt-dlp process.");
                return;
            }

            ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); }
                catch { /* already exited */ }
            });

            onStatus("Starting Download...");

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(ct)) is not null)
            {
                ct.ThrowIfCancellationRequested();

                var match = ProgressRegex().Match(line);
                if (match.Success && double.TryParse(match.Groups[1].Value, out var pct))
                {
                    onProgress(pct);
                    onStatus($"Downloading... {pct:F1}%");
                }
                else if (line.Contains("[ExtractAudio]") || line.Contains("Converting") || line.Contains("[Merger]"))
                {
                    onStatus(format == DownloadFormat.Mp3 ? "Converting to MP3..." : "Merging video and audio...");
                }
                else if (line.Contains("Destination:"))
                {
                    var filename = line.Split("Destination:").LastOrDefault()?.Trim() ?? "";
                    onStatus($"Saving: {filename}");
                }
            }

            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0)
            {
                onComplete(true, "✅ Download complete!");
            }
            else
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                onComplete(false, ParseError(stderr));
            }
        }
        catch (OperationCanceledException)
        {
            onComplete(false, "Download cancelled.");
        }
        catch (Exception ex)
        {
            onComplete(false, $"Error: {ex.Message}");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string ParseError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return "❌ Download failed unexpectedly.";

        if (stderr.Contains("Unsupported URL"))
            return "❌ This URL is not supported. Try a YouTube, TikTok, Instagram, or Facebook link.";

        if (stderr.Contains("Video unavailable") || stderr.Contains("This video is not available"))
            return "❌ This video is unavailable. It may be private, deleted, or region-locked.";

        if (stderr.Contains("Sign in to confirm your age") || stderr.Contains("age-restricted"))
            return "❌ This content is age-restricted and cannot be downloaded.";

        if (stderr.Contains("Private video") || stderr.Contains("is private"))
            return "❌ This is a private video. It cannot be downloaded.";

        if (stderr.Contains("copyright"))
            return "❌ This content was removed due to a copyright claim.";

        if (stderr.Contains("HTTP Error 403") || stderr.Contains("Forbidden"))
            return "❌ Access denied. The site blocked the request.";

        if (stderr.Contains("HTTP Error 404") || stderr.Contains("Not Found"))
            return "❌ Content not found. Check the URL and try again.";

        if (stderr.Contains("Unable to extract") || stderr.Contains("No video formats found"))
            return "❌ Could not extract media from this URL. The site may have changed or the link may be invalid.";

        if (stderr.Contains("network") || stderr.Contains("timed out") || stderr.Contains("Connection"))
            return "❌ Network error. Check your internet connection and try again.";

        if (stderr.Contains("already been downloaded"))
            return "File already exists in your download folder.";

        // Fall back to the actual error line from yt-dlp
        var errorLine = stderr
            .Split('\n')
            .LastOrDefault(l => l.Contains("ERROR:"))
            ?.Replace("ERROR:", "")
            .Trim();

        return !string.IsNullOrEmpty(errorLine)
            ? $"❌ {errorLine}"
            : $"❌ Download failed: {stderr.Trim().Split('\n').Last().Trim()}";
    }
}
