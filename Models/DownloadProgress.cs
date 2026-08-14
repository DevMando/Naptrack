namespace Naptrack.Models;

/// <summary>
/// A single progress tick parsed from a yt-dlp <c>--newline --progress</c> line.
/// Every field except <see cref="Percent"/> is best-effort: yt-dlp omits the size on some
/// extractors and prints "Unknown" for speed and ETA before the transfer settles.
/// </summary>
public readonly record struct DownloadProgress(
    double Percent,
    string TotalSize,
    string Speed,
    string Eta);

/// <summary>
/// What yt-dlp has revealed about the job so far, reported as soon as it is known rather than at
/// completion: with several downloads running at once, a truncated URL cannot tell two rows apart.
/// </summary>
/// <param name="PlaylistIndex">1-based position in a playlist, or 0 when not a playlist.</param>
public readonly record struct DownloadMetadata(
    string FileName,
    int PlaylistIndex,
    int PlaylistCount);

/// <summary>Outcome of a finished yt-dlp run.</summary>
/// <param name="FileName">
/// Bare name of the file actually written, when yt-dlp announced one. Empty if the run failed
/// before it picked a destination.
/// </param>
/// <param name="Retryable">
/// True only for failures a later attempt could plausibly survive — throttling, a blocked
/// request, a dropped connection. A deleted, private or age-restricted video is never retryable,
/// because re-running yt-dlp against it would fail identically after wasting the wait.
/// </param>
public readonly record struct DownloadResult(
    bool Success,
    string Message,
    string FileName,
    bool Retryable = false);
