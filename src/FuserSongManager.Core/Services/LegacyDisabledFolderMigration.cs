using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Services;

/// <summary>
/// Older versions kept disabled songs in <c>Paks\disabled_songs</c>, which Fuser still loads. Moves them to the
/// current disabled folder (outside Paks). Relative paths are preserved, so the library database stays valid.
/// </summary>
public static class LegacyDisabledFolderMigration
{
    /// <returns>Number of files moved.</returns>
    public static int Run(LibraryPaths paths)
    {
        var legacy = Path.Combine(paths.PaksDirectory, "disabled_songs");
        if (!Directory.Exists(legacy) || SamePath(legacy, paths.DisabledDirectory))
            return 0;

        var moved = 0;
        Directory.CreateDirectory(paths.DisabledDirectory);

        foreach (var source in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(paths.DisabledDirectory, Path.GetRelativePath(legacy, source));
            if (File.Exists(target))
                continue; // never overwrite; the leftover stays in the legacy folder

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target);
            moved++;
        }

        RemoveEmptyDirectories(legacy);
        return moved;
    }

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);

        if (!Directory.EnumerateFileSystemEntries(root).Any())
            Directory.Delete(root);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
