using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Services;

/// <summary>Starts Fuser the way v1 did: clears the custom song cache first, then launches per launcher type.</summary>
public sealed class GameLaunchService(IGameProcessService game, ISongFileService files)
{
    public LaunchResult Launch(AppSettings settings)
    {
        if (game.IsGameRunning())
            return new LaunchResult(LaunchOutcome.AlreadyRunning, "Fuser is already running.");
        if (string.IsNullOrWhiteSpace(settings.FuserDirectory))
            return new LaunchResult(LaunchOutcome.NotConfigured, "The Fuser folder is not configured.");

        files.ClearUnlockedSongsCache(LibraryPaths.FromSettings(settings));
        return game.Launch(settings);
    }
}
