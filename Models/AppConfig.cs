namespace Naptrack.Models;

public class AppConfig
{
    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music", "Naptrack");

    /// <summary>Last format picked, so the choice survives a restart.</summary>
    public DownloadFormat Format { get; set; } = DownloadFormat.Mp3;

    /// <summary>URLs recalled with the up arrow, oldest first.</summary>
    public List<string> RecentUrls { get; set; } = [];

    /// <summary>Latest stable version seen on nuget.org, cached so startup does not wait on the network.</summary>
    public string? LatestKnownVersion { get; set; }

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// Version string of the yt-dlp Naptrack manages, as reported by <c>--version</c>. Recorded so
    /// a refresh can be skipped when the published build has not moved on since the last check.
    /// </summary>
    public string? YtDlpVersion { get; set; }

    /// <summary>
    /// When the managed yt-dlp was last compared against the published build. Throttles the check
    /// to once a day: yt-dlp publishes most days, but a fresh binary matters per-session, not
    /// per-launch.
    /// </summary>
    public DateTimeOffset? YtDlpLastCheckUtc { get; set; }
}
