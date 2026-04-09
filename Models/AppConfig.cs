namespace Naptrack.Models;

public class AppConfig
{
    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music", "Naptrack");
}
