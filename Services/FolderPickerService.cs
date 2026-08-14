using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Naptrack.Services;

public class FolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        try
        {
            ProcessStartInfo psi;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; " +
                                "$dialog = New-Object System.Windows.Forms.FolderBrowserDialog; " +
                                "$dialog.Description = 'Select download folder'; " +
                                "$dialog.ShowNewFolderButton = $true; " +
                                "if ($dialog.ShowDialog() -eq 'OK') { $dialog.SelectedPath } else { '' }\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = "-e 'POSIX path of (choose folder with prompt \"Select download folder\")'",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                var useZenity = await CommandExistsAsync("zenity");
                psi = useZenity
                    ? new ProcessStartInfo
                    {
                        FileName = "zenity",
                        Arguments = "--file-selection --directory --title=\"Select download folder\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                    : new ProcessStartInfo
                    {
                        FileName = "kdialog",
                        Arguments = "--getexistingdirectory ~",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
            }

            using var process = Process.Start(psi);
            if (process is null) return null;

            var result = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 && !string.IsNullOrEmpty(result) ? result : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reveals <paramref name="path"/> in the system file manager. False if that was not possible.</summary>
    public bool OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            var opener =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "explorer.exe"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open"
                : "xdg-open";

            var psi = new ProcessStartInfo
            {
                FileName = opener,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // ArgumentList quotes for us, so a download folder containing spaces
            // does not arrive at the file manager split into several arguments.
            psi.ArgumentList.Add(path);

            using var process = Process.Start(psi);
            return process is not null;
        }
        catch
        {
            // A headless Linux box has no xdg-open, and the path may be uncreatable.
            // Neither is worth taking the UI down for: this runs straight off a click.
            return false;
        }
    }

    private static async Task<bool> CommandExistsAsync(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }
}
