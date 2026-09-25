namespace Naptrack.Services;

/// <summary>
/// Keeps the yt-dlp Naptrack downloads current.
///
/// The binary is the part of this app that rots. Sites change what they accept with no notice,
/// and a yt-dlp that worked last month fails today with an error that reads like the user has
/// been blocked -- extraction succeeds, then the media request comes back 403. Before this
/// existed, yt-dlp was fetched once on first run and never looked at again, so every install
/// eventually reached that state permanently, with nothing the user could do from inside the app.
/// </summary>
public class YtDlpUpdater
{
    /// <summary>
    /// Gap between automatic checks. yt-dlp publishes most days, but a build that is a day old
    /// has never been the problem, and the check costs a network round trip on every launch.
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly BinaryDownloader _downloader;
    private readonly DependencyChecker _depChecker;
    private readonly ConfigService _config;

    public YtDlpUpdater(BinaryDownloader downloader, DependencyChecker depChecker, ConfigService config)
    {
        _downloader = downloader;
        _depChecker = depChecker;
        _config = config;
    }

    public enum Outcome
    {
        /// <summary>Already current, or checked too recently to be worth asking again.</summary>
        UpToDate,

        /// <summary>A newer build was fetched. The caller must re-run the dependency check.</summary>
        Updated,

        /// <summary>
        /// A newer build could not be fetched, or a requested check could not reach GitHub at all.
        /// The old one still works.
        /// </summary>
        Failed,

        /// <summary>yt-dlp came from PATH, so it is not Naptrack's to replace.</summary>
        NotManaged,
    }

    /// <summary>
    /// Brings the managed yt-dlp up to date if it is due a check.
    /// </summary>
    /// <param name="force">
    /// Skips the once-a-day throttle, and compares the published build only against the version
    /// the binary itself reports, never the one recorded in the config. This is what the manual
    /// button does: someone who has just been told they are blocked wants the newest binary now,
    /// not a reminder that Naptrack looked yesterday.
    /// </param>
    public async Task<Outcome> EnsureCurrentAsync(
        bool force = false,
        Action<string>? onStatus = null,
        CancellationToken ct = default)
    {
        // A yt-dlp from PATH belongs to whatever installed it -- pip, brew, a package manager --
        // and overwriting it would be a surprise Naptrack has no right to spring. The one case
        // worth acting on is a system copy that has gone stale: a managed binary takes precedence
        // at the next check, so installing one is the only route back to working downloads that
        // does not involve touching someone else's install.
        var adopting = _depChecker.IsYtDlpInstalled && !_depChecker.IsYtDlpManaged;

        if (adopting)
        {
            if (!force && !_depChecker.IsYtDlpStale)
                return Outcome.NotManaged;
        }
        else if (!force && !IsCheckDue())
        {
            return Outcome.UpToDate;
        }

        onStatus?.Invoke(adopting
            ? "Installing an up-to-date yt-dlp..."
            : "Checking for a newer yt-dlp...");

        var latest = await _downloader.FetchLatestYtDlpVersionAsync(ct);

        // An unknown remote version means offline or rate limited, not "nothing newer". Recording
        // a check here would start a 24 hour timer on an answer that was never received.
        //
        // Adoption carries on regardless: the download URL resolves the newest build server-side
        // and needs nothing from the API, so a stale system copy still gets replaced.
        //
        // Unless the user asked. Then "could not check" has to be said as such: answering
        // "already up to date" to someone troubleshooting blocked downloads while offline sends
        // them looking everywhere except their connection.
        if (latest is null && !adopting)
            return force ? Outcome.Failed : Outcome.UpToDate;

        if (!adopting && MatchesInstalled(latest!, trustConfig: !force))
        {
            await RecordCheckAsync(latest);
            return Outcome.UpToDate;
        }

        if (latest is not null)
            onStatus?.Invoke($"Updating yt-dlp to {latest}...");

        var result = await DownloadAsync(onStatus, ct);

        // Recorded only on success, and only now: stamping the check before the bytes landed
        // would suppress the retry that a failed download specifically needs.
        if (result == Outcome.Updated)
            await RecordCheckAsync(latest);

        return result;
    }

    private async Task<Outcome> DownloadAsync(Action<string>? onStatus, CancellationToken ct)
    {
        var ok = await _downloader.DownloadYtDlpAsync(onStatus, ct);
        return ok ? Outcome.Updated : Outcome.Failed;
    }

    private bool IsCheckDue()
    {
        var last = _config.Config.YtDlpLastCheckUtc;
        return last is null || DateTimeOffset.UtcNow - last.Value >= CheckInterval;
    }

    /// <summary>
    /// Compares the published tag against what is actually installed, preferring the version the
    /// binary reports over the one in the config: a hand-replaced binary, or a config that was
    /// written before the download failed, would otherwise pin the comparison to a stale value.
    /// </summary>
    /// <param name="trustConfig">
    /// Whether the recorded version may stand in when the binary did not report one. Off for a
    /// forced check, where an unreadable version should mean "fetch it" rather than a guess.
    /// </param>
    private bool MatchesInstalled(string latest, bool trustConfig)
    {
        var installed = !string.IsNullOrWhiteSpace(_depChecker.YtDlpVersion)
            ? _depChecker.YtDlpVersion
            : trustConfig ? _config.Config.YtDlpVersion : null;

        return string.Equals(installed?.Trim(), latest.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task RecordCheckAsync(string? version)
    {
        _config.Config.YtDlpVersion = version;
        _config.Config.YtDlpLastCheckUtc = DateTimeOffset.UtcNow;

        try
        {
            await _config.SaveAsync();
        }
        catch
        {
            // An unwritable config costs an extra check next launch, which is not worth
            // interrupting startup over.
        }
    }
}
