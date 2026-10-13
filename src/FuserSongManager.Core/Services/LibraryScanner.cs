using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Services;

/// <summary>
/// Enumerates the enabled and disabled folders once each (v1 ran a recursive glob per file).
/// </summary>
public sealed class LibraryScanner : ILibraryScanner
{
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    public LibraryScan Scan(LibraryPaths paths)
    {
        var paks = new List<PakFile>();
        var duplicates = new List<PakFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var location in new[] { SongLocation.Enabled, SongLocation.Disabled })
        {
            foreach (var pak in ScanFolder(paths.RootOf(location), location))
            {
                if (seen.Add(pak.FileName))
                    paks.Add(pak);
                else
                    duplicates.Add(pak);
            }
        }

        return new LibraryScan(paks, duplicates);
    }

    private static IEnumerable<PakFile> ScanFolder(string root, SongLocation location)
    {
        var directory = new DirectoryInfo(root);
        if (!directory.Exists)
            return [];

        var files = directory.EnumerateFiles("*", Recursive).ToList();
        var signatures = files
            .Where(f => f.Extension.Equals(".sig", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.ChangeExtension(f.FullName, null))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return files
            .Where(f => f.Extension.Equals(".pak", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new PakFile(
                Path.GetFileNameWithoutExtension(f.Name),
                f.FullName,
                Path.GetRelativePath(root, f.FullName),
                location,
                f.Length,
                f.LastWriteTimeUtc,
                signatures.Contains(Path.ChangeExtension(f.FullName, null))))
            .ToList();
    }
}
