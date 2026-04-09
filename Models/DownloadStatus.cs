namespace Naptrack.Models;

public enum DownloadStatus
{
    Idle,
    Downloading,
    Converting,
    Complete,
    Error
}

public enum DownloadFormat
{
    Mp3,
    Mp4
}
