namespace Naptrack.Models;

public class AppConfig
{
    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music", "Naptrack");

    /// <summary>Latest stable version seen on nuget.org, cached so startup does not wait on the network.</summary>
    public string? LatestKnownVersion { get; set; }

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
}
