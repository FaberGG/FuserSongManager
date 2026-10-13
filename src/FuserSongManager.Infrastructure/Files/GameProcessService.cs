using System.Diagnostics;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Infrastructure.Files;

public sealed class GameProcessService : IGameProcessService
{
    // Process names used by v1 (Steam build and Epic/EOS build), without ".exe".
    private static readonly string[] ProcessNames = ["Fuser-Win64-Shipping", "FuserEOS-Win64-Shipping"];

    private const string SteamLaunchUri = "steam://run/1331440";

    public bool IsGameRunning() => ProcessNames.Any(name =>
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
            process.Dispose();
        return processes.Length > 0;
    });

    public LaunchResult Launch(AppSettings settings)
    {
        try
        {
            switch (settings.LauncherType)
            {
                case LauncherType.Steam:
                    Start(new ProcessStartInfo(SteamLaunchUri) { UseShellExecute = true });
                    return new LaunchResult(LaunchOutcome.Started);

                case LauncherType.Epic:
                    // v1 never verified the Epic launch URI; launch from the Epic Games Launcher instead.
                    return new LaunchResult(LaunchOutcome.EpicNotSupported,
                        "Launching Fuser through the Epic Games Launcher is not supported. Start it from the Epic Games Launcher.");

                default:
                    var executable = settings.ExecutablePath;
                    if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                        return new LaunchResult(LaunchOutcome.ExecutableMissing,
                            $"The game executable {(string.IsNullOrWhiteSpace(executable) ? "is not configured" : $"{executable} does not exist")}. Set it in Settings.");
                    // Shell execute so .bat launchers work; run from their own folder, as v1 did.
                    Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! });
                    return new LaunchResult(LaunchOutcome.Started);
            }
        }
        catch (Exception ex)
        {
            return new LaunchResult(LaunchOutcome.Failed, $"Could not start Fuser: {ex.Message}");
        }
    }

    private static void Start(ProcessStartInfo startInfo) => Process.Start(startInfo)?.Dispose();
}
