using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Naptrack.Models;

namespace Naptrack.Services;

public partial class YtDlpService
{
    private readonly DependencyChecker _depChecker;

    /// <summary>Longest this will wait on the stderr drain before abandoning it and returning.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    public YtDlpService(DependencyChecker depChecker)
    {
        _depChecker = depChecker;
    }

    // yt-dlp progress lines look like:
    //   [download]  45.2% of  3.50MiB at  1.20MiB/s ETA 00:02
    // Size, speed and ETA are matched separately rather than as one alternation-heavy pattern:
    // extractors omit fields and print a literal "Unknown" for others, and independent patterns
    // degrade one field at a time instead of dropping the whole line.
    // Anchored to the start of a [download] line. Unanchored, a percent sign anywhere in the
    // output matched -- including one in a title, so "[ExtractAudio] Destination: 100% Hits.mp3"
    // read as a progress tick and swallowed the Converting status.
    [GeneratedRegex(@"^\[download\]\s+(\d+(?:\.\d+)?)%")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"of\s+~?\s*([\d.]+\s*[KMGT]?i?B)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"at\s+([\d.]+\s*[KMGT]?i?B/s)", RegexOptions.IgnoreCase)]
    private static partial Regex SpeedRegex();

    [GeneratedRegex(@"ETA\s+((?:\d+:)?\d{1,2}:\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex EtaRegex();

    [GeneratedRegex(@"Destination:\s*(.+?)\s*$")]
    private static partial Regex DestinationRegex();

    [GeneratedRegex(@"Merging formats into\s+""(.+?)""")]
    private static partial Regex MergeTargetRegex();

    [GeneratedRegex(@"^\[download\]\s+(.+?)\s+has already been downloaded")]
    private static partial Regex AlreadyDownloadedRegex();

    // yt-dlp announces playlist position as "Downloading item 3 of 47"; older builds say "video".
    [GeneratedRegex(@"Downloading (?:item|video) (\d+) of (\d+)")]
    private static partial Regex PlaylistPositionRegex();

    public async Task DownloadAsync(
        string url,
        string outputDir,
        DownloadFormat format,
        bool wholePlaylist,
        Action<DownloadProgress> onProgress,
        Action<string> onStatus,
        Action<DownloadMetadata> onMetadata,
        Action<DownloadResult> onComplete,
        CancellationToken ct = default)
    {
        Process? process = null;
        Task<string> stderrTask = Task.FromResult("");
        var registration = default(CancellationTokenRegistration);

        // Tracked across the read loop so the completion message can name the file that was
        // actually written. For MP3 the last Destination line is the extracted audio, not the
        // intermediate container; for MP4 it is the merge target.
        var finalFile = "";
        var playlistIndex = 0;
        var playlistCount = 0;

        try
        {
            // Inside the try: an unwritable or invalid outputDir must surface through
            // onComplete, not as an exception the caller has no way to observe.
            Directory.CreateDirectory(outputDir);

            var psi = new ProcessStartInfo
            {
                FileName = _depChecker.YtDlpPath,
                WorkingDirectory = outputDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,

                // yt-dlp encodes what it prints with the locale's code page when stdout is a pipe,
                // which on Windows turns a Japanese or accented title into question marks before
                // it ever reaches the downloads list. Pinned to UTF-8 on both ends.
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            psi.Environment["PYTHONIOENCODING"] = "utf-8";

            // Every argument goes through ArgumentList, which quotes each one. The URL is text the
            // user pasted, and spliced into a single command line a link containing a quote could
            // close it and smuggle in options of its own -- --exec among them.
            var args = psi.ArgumentList;

            var ffmpegDir = Path.GetDirectoryName(_depChecker.FfmpegPath);
            if (!string.IsNullOrEmpty(ffmpegDir) && ffmpegDir != ".")
            {
                args.Add("--ffmpeg-location");
                args.Add(ffmpegDir);
            }

            // YouTube's player challenges are JavaScript, and yt-dlp now warns that extracting
            // without an engine to run them is deprecated and drops formats. It looks for deno on
            // its own; node and bun have to be named. Only passed when the probe confirmed this
            // build understands the flag -- an unknown option is a hard failure, not a warning.
            if (_depChecker is { SupportsJsRuntimes: true, JsRuntime: { } runtime })
            {
                args.Add("--js-runtimes");
                args.Add(runtime);
            }

            // Always explicit. Left to itself yt-dlp expands any URL carrying a "list=" parameter
            // into the whole playlist, which is most links copied from YouTube while a playlist is
            // open: asking for one track and getting two hundred is the worst surprise this app
            // can spring, so downloading a playlist has to be something the user opted into.
            if (wholePlaylist)
            {
                args.Add("--yes-playlist");
            }
            else
            {
                // --no-playlist only chooses the video when a URL names both a video and a
                // playlist. A link to a playlist alone ignores it and downloads everything, so
                // the item limit is what actually holds "one video" to one video.
                args.Add("--no-playlist");
                args.Add("--playlist-items");
                args.Add("1");
            }

            if (format == DownloadFormat.Mp3)
            {
                args.Add("-x");
                args.Add("--audio-format");
                args.Add("mp3");
                args.Add("--audio-quality");
                args.Add("0");
            }
            else
            {
                // MP4 streams first, since they merge without re-encoding. The trailing
                // fallbacks are for sites that publish no MP4 at all, which otherwise failed
                // outright with "Requested format is not available".
                args.Add("-f");
                args.Add("bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/bestvideo*+bestaudio/best");
                args.Add("--merge-output-format");
                args.Add("mp4");
            }

            args.Add("-o");
            args.Add("%(title)s.%(ext)s");
            args.Add("--newline");
            args.Add("--progress");

            // Ends option parsing, so nothing in the URL can ever be read as a flag.
            args.Add("--");
            args.Add(url);

            process = Process.Start(psi);
            if (process is null)
            {
                onComplete(new DownloadResult(false, "Failed to start yt-dlp process.", ""));
                return;
            }

            // Killing the child is what actually unblocks the read loop below. On Windows a read
            // on a redirected pipe is not reliably cancellable, so ReadLineAsync does not throw
            // when the token trips: the loop ends only because the pipe closes with the process.
            //
            // This callback runs synchronously on whichever thread calls Cancel(), and
            // Kill(entireProcessTree) walks the whole system process table on Windows, so callers
            // must not cancel from a thread that has to stay responsive.
            registration = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* already exited, or the handle is gone */ }
            });

            // Drain stderr concurrently rather than after exit. yt-dlp writes warnings there
            // while it runs, and a full pipe buffer (4KB on Windows) blocks the child mid-write,
            // which also stops it emitting stdout: the read loop below would then wait forever
            // on output that never comes. Same failure mode as DependencyChecker.CheckAsync.
            stderrTask = process.StandardError.ReadToEndAsync(ct);

            onStatus("Starting download...");

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(ct)) is not null)
            {
                // Destination lines are scanned before the progress branch so that a filename
                // announced on the same line as other output is never missed.
                var previousFile = finalFile;

                var destination = DestinationRegex().Match(line);
                if (destination.Success)
                    finalFile = destination.Groups[1].Value;

                var merged = MergeTargetRegex().Match(line);
                if (merged.Success)
                    finalFile = merged.Groups[1].Value;

                var already = AlreadyDownloadedRegex().Match(line);
                if (already.Success)
                    finalFile = already.Groups[1].Value;

                var position = PlaylistPositionRegex().Match(line);
                var movedInPlaylist = position.Success
                    && int.TryParse(position.Groups[1].Value, out var index)
                    && int.TryParse(position.Groups[2].Value, out var count)
                    && (index != playlistIndex || count != playlistCount);

                if (movedInPlaylist)
                {
                    playlistIndex = int.Parse(position.Groups[1].Value);
                    playlistCount = int.Parse(position.Groups[2].Value);
                }

                // Reported as soon as it is known, not just at completion: with several
                // downloads running at once a truncated URL cannot tell two rows apart.
                if (movedInPlaylist || !string.Equals(previousFile, finalFile, StringComparison.Ordinal))
                    onMetadata(new DownloadMetadata(Path.GetFileName(finalFile), playlistIndex, playlistCount));

                var progress = ProgressRegex().Match(line);
                if (progress.Success &&
                    double.TryParse(progress.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                {
                    // Invariant culture is required: yt-dlp always emits "45.2", which a
                    // comma-decimal locale would otherwise parse as 452.
                    onProgress(new DownloadProgress(
                        pct,
                        FirstGroup(SizeRegex(), line),
                        FirstGroup(SpeedRegex(), line),
                        FirstGroup(EtaRegex(), line)));
                    continue;
                }

                if (line.Contains("[ExtractAudio]") || line.Contains("Converting") || line.Contains("[Merger]"))
                    onStatus(format == DownloadFormat.Mp3 ? "Converting to MP3..." : "Merging video and audio...");
                else if (destination.Success)
                    onStatus($"Saving {Path.GetFileName(finalFile)}");
            }

            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0)
            {
                onComplete(new DownloadResult(true, "", Path.GetFileName(finalFile)));
            }
            else
            {
                var (message, retryable) = ParseError(await stderrTask);
                onComplete(new DownloadResult(false, message, Path.GetFileName(finalFile), retryable));
            }
        }
        catch (OperationCanceledException)
        {
            onComplete(new DownloadResult(false, "Cancelled", Path.GetFileName(finalFile)));
        }
        catch (Exception ex)
        {
            onComplete(new DownloadResult(false, ex.Message, Path.GetFileName(finalFile)));
        }
        finally
        {
            // Dropped before the process handle it closes over, so a late cancellation cannot
            // reach into a disposed Process.
            registration.Dispose();

            // Observe the drain task. Cancelling a download faults it, and an unobserved faulted
            // task would otherwise resurface on the finalizer thread.
            //
            // Bounded, because on Windows this read is not reliably cancellable either: if the
            // kill somehow failed, an unbounded await here would hold this method open forever
            // and leave the caller's row stuck mid-download with no way out.
            try { await stderrTask.WaitAsync(DrainTimeout, CancellationToken.None); }
            catch { /* already reported above, timed out, or faulted by cancellation */ }

            process?.Dispose();
        }
    }

    private static string FirstGroup(Regex regex, string line)
    {
        var match = regex.Match(line);
        return match.Success ? match.Groups[1].Value.Replace(" ", "") : "";
    }

    // Messages carry no status glyph: the downloads list owns the leading marker so that a
    // failure, a cancellation and a completion all align in the same column.
    //
    // Only the explicitly transient cases are marked retryable. An unrecognised error stays
    // non-retryable on purpose: a wrong guess costs the user the full backoff twice over and
    // ends at the same message it started with.
    private static (string Message, bool Retryable) ParseError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return ("Download failed unexpectedly.", false);

        var errorLines = stderr
            .Split('\n')
            .Where(l => l.Contains("ERROR:"))
            .ToArray();

        // Classified on the ERROR lines when there are any. stderr also carries warnings, and a
        // playlist routinely warns about hidden "Private video" or "Video unavailable" entries:
        // matched against everything, a run that really died of a 429 was reported as an
        // unavailable video and never retried.
        var text = errorLines.Length > 0 ? string.Join('\n', errorLines) : stderr;

        return Classify(text) ?? Fallback(stderr, errorLines);
    }

    private static (string Message, bool Retryable)? Classify(string stderr)
    {
        if (stderr.Contains("Unsupported URL"))
            return ("This URL is not supported. Try a YouTube, TikTok, Instagram, or Facebook link.", false);

        if (stderr.Contains("Video unavailable") || stderr.Contains("This video is not available"))
            return ("This video is unavailable. It may be private, deleted, or region-locked.", false);

        if (stderr.Contains("Sign in to confirm your age") || stderr.Contains("age-restricted"))
            return ("This content is age-restricted and cannot be downloaded.", false);

        if (stderr.Contains("Private video") || stderr.Contains("is private"))
            return ("This is a private video. It cannot be downloaded.", false);

        if (stderr.Contains("copyright"))
            return ("This content was removed due to a copyright claim.", false);

        // Throttling. Retryable, and the case most likely to clear on its own.
        if (stderr.Contains("HTTP Error 429") || stderr.Contains("Too Many Requests"))
            return ("Rate limited by the site.", true);

        // The bot check names itself, and no amount of retrying clears it: the request needs to
        // carry a signed-in session, which yt-dlp can lift from a browser profile.
        if (stderr.Contains("Sign in to confirm you’re not a bot")
            || stderr.Contains("Sign in to confirm you're not a bot")
            || stderr.Contains("confirm you are not a bot"))
        {
            return ("The site asked for a sign-in check. Update yt-dlp below, or try again later.", false);
        }

        // A 403 on a media request is an expired or rejected URL signature far more often than a
        // real permission decision, and yt-dlp resumes the partial file on the next attempt.
        //
        // It is also exactly how an out-of-date yt-dlp fails: extraction succeeds, formats are
        // listed, and only the transfer is refused. The message names that, because the retries
        // will not fix it and the update almost always does.
        if (stderr.Contains("HTTP Error 403") || stderr.Contains("Forbidden"))
            return ("Access denied. If this keeps happening, update yt-dlp below.", true);

        if (stderr.Contains("HTTP Error 5") || stderr.Contains("Internal Server Error")
            || stderr.Contains("Bad Gateway") || stderr.Contains("Service Unavailable"))
            return ("The site returned a server error.", true);

        if (stderr.Contains("HTTP Error 404") || stderr.Contains("Not Found"))
            return ("Content not found. Check the URL and try again.", false);

        if (stderr.Contains("Unable to extract") || stderr.Contains("No video formats found"))
            return ("Could not extract media from this URL. The site may have changed or the link may be invalid.", false);

        if (stderr.Contains("network") || stderr.Contains("timed out") || stderr.Contains("Connection")
            || stderr.Contains("Remote end closed") || stderr.Contains("Temporary failure in name resolution"))
            return ("Network error. Check your internet connection and try again.", true);

        if (stderr.Contains("already been downloaded"))
            return ("File already exists in your download folder.", false);

        return null;
    }

    // Falls back to the actual error line from yt-dlp.
    private static (string Message, bool Retryable) Fallback(string stderr, string[] errorLines)
    {
        var errorLine = errorLines.LastOrDefault()?.Replace("ERROR:", "").Trim();

        return (!string.IsNullOrEmpty(errorLine)
            ? errorLine
            : $"Download failed: {stderr.Trim().Split('\n').Last().Trim()}", false);
    }
}
