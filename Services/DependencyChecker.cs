using System.Diagnostics;

namespace Naptrack.Services;

public class DependencyChecker
{
    private readonly BinaryDownloader _downloader;

    public DependencyChecker(BinaryDownloader downloader)
    {
        _downloader = downloader;
    }

    public bool IsYtDlpInstalled { get; private set; }
    public bool IsFfmpegInstalled { get; private set; }
    public string YtDlpVersion { get; private set; } = "";
    public string YtDlpPath { get; private set; } = "";
    public string FfmpegPath { get; private set; } = "";
    public bool Checked { get; private set; }

    public async Task CheckAsync()
    {
        // Check local bin folder first, then system PATH
        if (_downloader.YtDlpExists)
        {
            var (ok, ver) = await CheckCommandAsync(_downloader.YtDlpPath, "--version");
            if (ok)
            {
                IsYtDlpInstalled = true;
                YtDlpVersion = ver;
                YtDlpPath = _downloader.YtDlpPath;
            }
        }

        if (!IsYtDlpInstalled)
        {
            var (ok, ver) = await CheckCommandAsync("yt-dlp", "--version");
            IsYtDlpInstalled = ok;
            YtDlpVersion = ver;
            YtDlpPath = ok ? "yt-dlp" : "";
        }

        if (_downloader.FfmpegExists)
        {
            var (ok, _) = await CheckCommandAsync(_downloader.FfmpegPath, "-version");
            if (ok)
            {
                IsFfmpegInstalled = true;
                FfmpegPath = _downloader.FfmpegPath;
            }
        }

        if (!IsFfmpegInstalled)
        {
            var (ok, _) = await CheckCommandAsync("ffmpeg", "-version");
            IsFfmpegInstalled = ok;
            FfmpegPath = ok ? "ffmpeg" : "";
        }

        Checked = true;
    }

    private static async Task<(bool installed, string output)> CheckCommandAsync(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, "");

            var output = (await process.StandardOutput.ReadLineAsync()) ?? "";
            await process.WaitForExitAsync();
            return (process.ExitCode == 0, output);
        }
        catch
        {
            return (false, "");
        }
    }
}
