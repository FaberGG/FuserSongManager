using System.Diagnostics;

namespace FuserSongManager.UI.Services;

public sealed class ShellService : IShellService
{
    public void ShowInFileManager(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Opens Explorer with the file selected; falls back to the folder if the file is gone.
            var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false })?.Dispose();
        }
        else if (Path.GetDirectoryName(path) is { } folder)
        {
            OpenFolder(folder);
        }
    }

    public void OpenFolder(string folder) =>
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
}
