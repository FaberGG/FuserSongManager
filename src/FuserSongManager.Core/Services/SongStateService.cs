using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Services;

/// <summary>Enables and disables songs by moving their files, one by one or in bulk (RF-EST-01..08).</summary>
public sealed class SongStateService(ISongFileService files, ISongRepository repository, IGameProcessService game)
{
    public async Task<IReadOnlyList<StateChangeResult>> SetLocationAsync(
        IReadOnlyCollection<Song> songs,
        SongLocation target,
        LibraryPaths paths,
        CancellationToken ct = default)
    {
        // The game keeps the paks open and caches the custom song list: never touch files while it runs.
        if (game.IsGameRunning())
            return songs.Select(s => new StateChangeResult(s.FileName, StateChangeOutcome.GameRunning, "Fuser is running. Close the game before enabling or disabling songs.")).ToList();

        var results = new List<StateChangeResult>(songs.Count);
        var moved = new List<Song>();

        foreach (var song in songs)
        {
            ct.ThrowIfCancellationRequested();
            if (song.Location == target)
            {
                results.Add(new StateChangeResult(song.FileName, StateChangeOutcome.AlreadyThere));
                continue;
            }
            if (song.RelativePath is null)
            {
                results.Add(new StateChangeResult(song.FileName, StateChangeOutcome.NotIndexed, "The song has not been scanned yet. Rescan the library first."));
                continue;
            }

            var result = files.Move(song, target, paths);
            results.Add(result);
            if (result.Outcome == StateChangeOutcome.Moved)
                moved.Add(song with { Location = target });
        }

        if (moved.Count > 0)
        {
            await repository.SaveScanResultsAsync(moved, CancellationToken.None);
            files.ClearUnlockedSongsCache(paths);
        }

        return results;
    }
}
