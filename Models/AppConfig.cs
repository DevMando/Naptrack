namespace Naptrack.Models;

public class AppConfig
{
    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music", "Naptrack");

    /// <summary>Last format picked, so the choice survives a restart.</summary>
    public DownloadFormat Format { get; set; } = DownloadFormat.Mp3;

    /// <summary>URLs recalled with the up arrow, oldest first.</summary>
    public List<string> RecentUrls { get; set; } = [];

    /// <summary>Completed downloads shown in the list on a fresh start, oldest first.</summary>
    public List<RecentDownload> RecentDownloads { get; set; } = [];

    /// <summary>Latest stable version seen on nuget.org, cached so startup does not wait on the network.</summary>
    public string? LatestKnownVersion { get; set; }

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
}

/// <summary>A finished download, kept only so the list is not empty on a fresh start.</summary>
public class RecentDownload
{
    public string FileName { get; set; } = "";
    public string Folder { get; set; } = "";
}
