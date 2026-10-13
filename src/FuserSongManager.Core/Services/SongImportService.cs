using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Services;

/// <summary>Imports songs from archives or loose paks, then syncs so the new songs show up with their metadata.</summary>
public sealed class SongImportService(
    IArchiveImporter importer,
    IGameProcessService game,
    ISongFileService files,
    LibrarySyncService sync)
{
    public async Task<(ImportResult Import, SyncResult? Sync)> ImportAsync(
        IReadOnlyList<string> sources,
        LibraryPaths paths,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (game.IsGameRunning())
            return (ImportResult.Blocked(new ImportIssue("", null, ImportIssueKind.GameRunning, "Fuser is running. Close the game before adding songs.")), null);

        var import = await importer.ImportAsync(sources, paths, options ?? new ImportOptions(), progress, ct);
        if (import.Imported.Count == 0)
            return (import, null);

        files.ClearUnlockedSongsCache(paths);
        // Replaced paks keep their name but change size/date, so the sync re-reads them too.
        var syncResult = await sync.SyncAsync(paths, ct: CancellationToken.None);
        return (import, syncResult);
    }
}
