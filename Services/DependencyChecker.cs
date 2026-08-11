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

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private static async Task<(bool installed, string output)> CheckCommandAsync(string command, string args)
    {
        Process? process = null;

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

            process = Process.Start(psi);
            if (process is null)
                return (false, "");

            using var timeout = new CancellationTokenSource(ProbeTimeout);

            // Drain both pipes before waiting on exit. `ffmpeg -version` writes several KB
            // (the configuration line alone runs past 2000 chars), which overflows the 4KB
            // Windows pipe buffer and deadlocks a read-one-line-then-wait sequence.
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

            await Task.WhenAll(stdout, stderr);
            await process.WaitForExitAsync(timeout.Token);

            var firstLine = stdout.Result.Split('\n', 2)[0].TrimEnd('\r');
            return (process.ExitCode == 0, firstLine);
        }
        catch
        {
            // A missing binary, a broken one that cannot load its DLLs, or a probe that
            // outlived ProbeTimeout all mean "not usable".
            TryKill(process);
            return (false, "");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort
        }
    }
}
