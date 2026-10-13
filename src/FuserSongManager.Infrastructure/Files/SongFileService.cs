using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;
using Serilog;

namespace FuserSongManager.Infrastructure.Files;

public sealed class SongFileService : ISongFileService
{
    private const string UnlockedSongsCache = "customSongsUnlocked_P";

    public StateChangeResult Move(Song song, SongLocation target, LibraryPaths paths)
    {
        if (song.RelativePath is null)
            return new StateChangeResult(song.FileName, StateChangeOutcome.NotIndexed, "The song has not been scanned yet. Rescan the library first.");

        var sourceRoot = paths.RootOf(song.Location);
        var targetRoot = paths.RootOf(target);
        if (!TryResolve(sourceRoot, song.RelativePath, out var sourcePak) || !TryResolve(targetRoot, song.RelativePath, out var targetPak))
            return Fail(song, $"Refusing to move {song.RelativePath}: the path points outside the songs folders.");

        var sourceSig = Path.ChangeExtension(sourcePak, ".sig");
        var targetSig = Path.ChangeExtension(targetPak, ".sig");

        if (!File.Exists(sourcePak))
            return Fail(song, $"{sourcePak} no longer exists. Rescan the library.");

        var hasSignature = File.Exists(sourceSig);
        if (target == SongLocation.Enabled && !hasSignature)
            return new StateChangeResult(song.FileName, StateChangeOutcome.MissingSignature,
                $"{Path.GetFileName(sourceSig)} is missing next to the .pak, so the game would not load this song. It stays disabled.");

        if (File.Exists(targetPak) || File.Exists(targetSig))
            return new StateChangeResult(song.FileName, StateChangeOutcome.DestinationExists,
                $"{(File.Exists(targetPak) ? targetPak : targetSig)} already exists. Nothing was overwritten; remove the duplicate by hand.");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPak)!);
            File.Move(sourcePak, targetPak);
        }
        catch (Exception ex)
        {
            return Fail(song, $"Could not move {sourcePak}: {ex.Message}");
        }

        if (hasSignature)
        {
            try
            {
                File.Move(sourceSig, targetSig);
            }
            catch (Exception ex)
            {
                return Rollback(song, sourcePak, targetPak, sourceSig, ex);
            }
        }

        DeleteEmptyFolders(Path.GetDirectoryName(sourcePak)!, sourceRoot);
        return new StateChangeResult(song.FileName, StateChangeOutcome.Moved);
    }

    public void ClearUnlockedSongsCache(LibraryPaths paths)
    {
        foreach (var extension in new[] { ".pak", ".sig" })
        {
            var path = Path.Combine(paths.PaksDirectory, UnlockedSongsCache + extension);
            try
            {
                File.Delete(path); // no-op if it does not exist
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not delete {Path}; the game may show an outdated song list", path);
            }
        }
    }

    private static StateChangeResult Rollback(Song song, string sourcePak, string targetPak, string sourceSig, Exception cause)
    {
        try
        {
            File.Move(targetPak, sourcePak);
            return Fail(song, $"Could not move {sourceSig}: {cause.Message}. The change was undone; both files are where they were.");
        }
        catch (Exception rollbackError)
        {
            Log.Error(rollbackError, "Rollback failed for {Song}: {Pak} is separated from {Sig}", song.FileName, targetPak, sourceSig);
            return Fail(song, $"Could not move {sourceSig} ({cause.Message}) and could not undo the move of the .pak ({rollbackError.Message}). " +
                              $"Move {targetPak} back next to its .sig by hand.");
        }
    }

    /// <summary>Removes folders left empty by the move, up to (not including) the songs root.</summary>
    private static void DeleteEmptyFolders(string folder, string root)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var current = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            while (current.Length > rootFull.Length
                   && current.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   && !Directory.EnumerateFileSystemEntries(current).Any())
            {
                Directory.Delete(current);
                current = Path.GetDirectoryName(current)!;
            }
        }
        catch (IOException)
        {
            // Best effort only: an empty folder left behind is harmless.
        }
    }

    /// <summary>Combines root and relative path, rejecting anything that would land outside the root (RNF-05).</summary>
    private static bool TryResolve(string root, string relativePath, out string fullPath)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        fullPath = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        return fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }

    private static StateChangeResult Fail(Song song, string message) => new(song.FileName, StateChangeOutcome.Failed, message);
}
