namespace FuserSongManager.Core.Models;

public sealed record AppSettings
{
    /// <summary>Fuser install folder (the one containing <c>Fuser.exe</c>).</summary>
    public string? FuserDirectory { get; init; }

    /// <summary>Overrides the default <c>Content/Paks/disabled_songs</c> folder.</summary>
    public string? DisabledSongsDirectory { get; init; }

    public LauncherType LauncherType { get; init; } = LauncherType.Steam;

    /// <summary>Executable or batch file used to start the game with <see cref="LauncherType.Local"/>.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Paks read at the same time during a scan. Null uses <see cref="LibraryPaths.DefaultParallelism"/>.</summary>
    public int? ScanParallelism { get; init; }
}

/// <summary>Folders derived from the Fuser install folder.</summary>
public sealed record LibraryPaths(string PaksDirectory, string EnabledDirectory, string DisabledDirectory)
{
    public static int DefaultParallelism => Math.Max(2, Environment.ProcessorCount / 2);

    public static LibraryPaths FromFuserDirectory(string fuserDirectory, string? disabledOverride = null)
    {
        var paks = Path.Combine(fuserDirectory, "Fuser", "Content", "Paks");
        return new LibraryPaths(
            paks,
            Path.Combine(paks, "custom_songs"),
            string.IsNullOrWhiteSpace(disabledOverride) ? Path.Combine(paks, "disabled_songs") : disabledOverride);
    }

    public static LibraryPaths FromSettings(AppSettings settings) =>
        FromFuserDirectory(
            settings.FuserDirectory ?? throw new InvalidOperationException("The Fuser folder is not configured."),
            settings.DisabledSongsDirectory);

    public string RootOf(SongLocation location) =>
        location == SongLocation.Enabled ? EnabledDirectory : DisabledDirectory;
}
