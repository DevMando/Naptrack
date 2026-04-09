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

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start("explorer.exe", path);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start("open", path);
        else
            Process.Start("xdg-open", path);
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
