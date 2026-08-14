namespace Naptrack.Models;

/// <summary>
/// One row in the downloads list. Created the moment a URL is submitted and kept until it is
/// trimmed off the end of the finished history, so the UI can render queued, running and
/// completed downloads from a single collection.
/// </summary>
public sealed class DownloadItem
{
    public required int Id { get; init; }
    public required string Url { get; init; }
    public required DownloadFormat Format { get; init; }

    /// <summary>Whether the user opted this download into expanding its playlist.</summary>
    public bool WholePlaylist { get; init; }

    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public double Percent { get; set; }
    public string Speed { get; set; } = "";
    public string Eta { get; set; } = "";

    /// <summary>File yt-dlp is writing, once it announces a destination. Empty until then.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Folder the file landed in, captured at completion.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Failure detail, or the "already downloaded" notice. Carries no status glyph.</summary>
    public string Message { get; set; } = "";

    /// <summary>1-based position within a playlist, and its total. Both 0 for a single video.</summary>
    public int PlaylistIndex { get; set; }
    public int PlaylistCount { get; set; }

    public bool IsPlaylist => PlaylistCount > 1;

    /// <summary>1-based attempt number, incremented for each retry after a transient failure.</summary>
    public int Attempt { get; set; } = 1;

    /// <summary>Seconds left in the current retry backoff, for the countdown in the row.</summary>
    public int RetryIn { get; set; }

    /// <summary>Cancels the queue wait if still pending, or kills yt-dlp if already running.</summary>
    public CancellationTokenSource Cts { get; } = new();

    /// <summary>
    /// True once the background run has fully unwound. A cancelled row is shown as finished
    /// immediately, while its process may still be tearing down, so only a settled item's token
    /// source is safe to dispose.
    /// </summary>
    public bool Settled { get; set; }

    public bool IsActive =>
        Status is DownloadStatus.Queued or DownloadStatus.Downloading
            or DownloadStatus.Converting or DownloadStatus.Retrying;

    /// <summary>Best label available: the real filename once known, otherwise the source URL.</summary>
    public string Label => string.IsNullOrEmpty(FileName) ? Url : FileName;
}
