namespace Naptrack.Models;

public enum DownloadStatus
{
    /// <summary>Accepted, but waiting on a free slot before yt-dlp is started.</summary>
    Queued,
    Downloading,
    Converting,
    /// <summary>Waiting out the backoff between attempts after a transient failure.</summary>
    Retrying,
    Complete,
    Cancelled,
    Error
}

public enum DownloadFormat
{
    Mp3,
    Mp4
}
