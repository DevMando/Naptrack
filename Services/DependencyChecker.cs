using System.Diagnostics;

namespace Naptrack.Services;

public class DependencyChecker
{
    private readonly BinaryDownloader _downloader;

    public DependencyChecker(BinaryDownloader downloader)
    {
        _downloader = downloader;
    }

    /// <summary>
    /// How old a yt-dlp build may be before Naptrack stops trusting it. Sites change what they
    /// accept on no schedule, and a build past this point is the single most likely explanation
    /// for a download that fails with what looks like a block.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(14);

    public bool IsYtDlpInstalled { get; private set; }
    public bool IsFfmpegInstalled { get; private set; }
    public string YtDlpVersion { get; private set; } = "";
    public string YtDlpPath { get; private set; } = "";
    public string FfmpegPath { get; private set; } = "";
    public bool Checked { get; private set; }

    /// <summary>
    /// True when the yt-dlp in use is the copy under Naptrack's own bin directory, rather than
    /// one found on PATH. Only a managed copy can be refreshed from inside the app: replacing a
    /// binary someone installed through pip, brew or a package manager is not Naptrack's to do.
    /// </summary>
    public bool IsYtDlpManaged { get; private set; }

    /// <summary>
    /// Release date encoded in the yt-dlp version string, or null when it did not parse. Both
    /// channels are date-versioned -- stable as "2026.07.04", nightly as "2026.08.18.122307" --
    /// so the build date is available without asking the network anything.
    /// </summary>
    public DateOnly? YtDlpReleaseDate => ParseReleaseDate(YtDlpVersion);

    public bool IsYtDlpStale =>
        YtDlpReleaseDate is { } date
        && DateTime.UtcNow.Date - date.ToDateTime(TimeOnly.MinValue) > StaleAfter;

    /// <summary>
    /// Whether this yt-dlp understands <c>--js-runtimes</c>. Probed rather than assumed: the flag
    /// is recent, and passing an unknown option to an older build is a hard failure rather than a
    /// warning, which would break downloads for anyone on a system-installed yt-dlp.
    /// </summary>
    public bool SupportsJsRuntimes { get; private set; }

    /// <summary>
    /// A JavaScript engine on PATH that yt-dlp will not find by itself, or null. yt-dlp needs one
    /// to run YouTube's player challenges and now warns that extraction without it is deprecated;
    /// it auto-detects deno only, so node and bun have to be pointed at explicitly.
    /// </summary>
    public string? JsRuntime { get; private set; }

    public async Task CheckAsync()
    {
        // Everything is resolved into locals and published in one go at the end.
        //
        // This runs more than once per session -- after an update, and whenever setup is retried
        // -- so the previous answer cannot simply be carried forward. But clearing the fields up
        // front and filling them back in as each probe returns is worse: the probes spawn
        // processes and take a second or more, and the render pump repaints ten times a second
        // throughout. A UI reading these mid-check saw "not installed" and told the user setup
        // had failed, moments after a download that had actually succeeded.
        var ytDlpInstalled = false;
        var ytDlpManaged = false;
        var ytDlpVersion = "";
        var ytDlpPath = "";

        // Check local bin folder first, then system PATH
        if (_downloader.YtDlpExists)
        {
            var (ok, ver) = await CheckCommandAsync(_downloader.YtDlpPath, "--version");
            if (ok)
            {
                ytDlpInstalled = true;
                ytDlpManaged = true;
                ytDlpVersion = ver;
                ytDlpPath = _downloader.YtDlpPath;
            }
        }

        if (!ytDlpInstalled)
        {
            var (ok, ver) = await CheckCommandAsync("yt-dlp", "--version");
            ytDlpInstalled = ok;
            ytDlpManaged = false;
            ytDlpVersion = ver;
            ytDlpPath = ok ? "yt-dlp" : "";
        }

        var (supportsJsRuntimes, jsRuntime) = ytDlpInstalled
            ? await ProbeJsRuntimeAsync(ytDlpPath)
            : (false, null);

        var ffmpegInstalled = false;
        var ffmpegPath = "";

        if (_downloader.FfmpegExists)
        {
            var (ok, _) = await CheckCommandAsync(_downloader.FfmpegPath, "-version");
            if (ok)
            {
                ffmpegInstalled = true;
                ffmpegPath = _downloader.FfmpegPath;
            }
        }

        if (!ffmpegInstalled)
        {
            var (ok, _) = await CheckCommandAsync("ffmpeg", "-version");
            ffmpegInstalled = ok;
            ffmpegPath = ok ? "ffmpeg" : "";
        }

        IsYtDlpInstalled = ytDlpInstalled;
        IsYtDlpManaged = ytDlpManaged;
        YtDlpVersion = ytDlpVersion;
        YtDlpPath = ytDlpPath;
        SupportsJsRuntimes = supportsJsRuntimes;
        JsRuntime = jsRuntime;
        IsFfmpegInstalled = ffmpegInstalled;
        FfmpegPath = ffmpegPath;

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

    /// <summary>
    /// Reads the build date out of a yt-dlp version string. Anything that does not start with
    /// three numeric components is treated as unknown rather than guessed at, which leaves the
    /// binary considered fresh: a version this cannot read is not evidence that it is old.
    /// </summary>
    private static DateOnly? ParseReleaseDate(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var parts = version.Trim().Split('.');
        if (parts.Length < 3)
            return null;

        if (!int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var month)
            || !int.TryParse(parts[2], out var day))
        {
            return null;
        }

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private async Task<(bool Supported, string? Runtime)> ProbeJsRuntimeAsync(string ytDlpPath)
    {
        // A --help that will not run says nothing about flag support, and HelpMentionsJsRuntimes
        // already reports false for that case: the flag is an optimisation, and going without it
        // costs only format coverage.
        if (!await HelpMentionsJsRuntimesAsync(ytDlpPath))
            return (false, null);

        // deno is omitted on purpose: yt-dlp enables it by default, so naming it would only
        // restrict the set it was already going to consider.
        string[] candidates = ["node", "bun"];

        foreach (var candidate in candidates)
        {
            var (ok, _) = await CheckCommandAsync(candidate, "--version");
            if (ok)
                return (true, candidate);
        }

        return (true, null);
    }

    private async Task<bool> HelpMentionsJsRuntimesAsync(string ytDlpPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = "--help",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            using var timeout = new CancellationTokenSource(ProbeTimeout);

            // Drained in full for the same reason as every other probe here: --help runs to tens
            // of kilobytes, far past the pipe buffer.
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

            await Task.WhenAll(stdout, stderr);
            await process.WaitForExitAsync(timeout.Token);

            return stdout.Result.Contains("--js-runtimes", StringComparison.Ordinal);
        }
        catch
        {
            return false;
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
