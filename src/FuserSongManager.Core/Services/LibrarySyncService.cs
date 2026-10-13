using System.Diagnostics;
using System.Threading.Channels;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Services;

public sealed record SyncOptions
{
    /// <summary>Re-read every pak even if its size and date did not change (RF-BIB-09).</summary>
    public bool ForceReread { get; init; }

    public int Parallelism { get; init; } = LibraryPaths.DefaultParallelism;

    /// <summary>Songs written to the database per transaction.</summary>
    public int BatchSize { get; init; } = 100;
}

public sealed record SyncProgress(int Processed, int Total, string? FileName);

/// <param name="ReadProblems">Songs read in this sync that failed or were read partially, with the reason in <see cref="Song.ReadError"/>.</param>
public sealed record SyncResult(
    int Added,
    int Reread,
    int Updated,
    int Removed,
    int Unchanged,
    IReadOnlyList<Song> ReadProblems,
    IReadOnlyList<PakFile> Duplicates,
    TimeSpan Elapsed)
{
    public int PaksRead => Added + Reread;
    public int Failed => ReadProblems.Count(s => s.ReadStatus == ReadStatus.Failed);
}

/// <summary>
/// Brings the database in line with the song folders: reads only new or modified paks (in parallel),
/// updates the location of moved ones without reading them, and drops the ones that disappeared.
/// </summary>
public sealed class LibrarySyncService(ILibraryScanner scanner, ISongMetadataReader reader, ISongRepository repository)
{
    public const string PatchedDlcAuthor = "Harmonix";

    public async Task<SyncResult> SyncAsync(
        LibraryPaths paths,
        SyncOptions? options = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new SyncOptions();
        var stopwatch = Stopwatch.StartNew();

        var scan = await Task.Run(() => scanner.Scan(paths), ct);
        var existing = (await repository.GetAllAsync(ct)).ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);
        var plan = SyncPlan.Create(scan, existing, options.ForceReread);

        if (plan.Removed.Count > 0)
            await repository.DeleteAsync(plan.Removed, ct);
        if (plan.Updated.Count > 0)
            await repository.SaveScanResultsAsync(plan.Updated, ct);

        var problems = await ReadAndSaveAsync(plan.ToRead, options, progress, ct);

        return new SyncResult(
            Added: plan.ToRead.Count(r => r.Existing is null),
            Reread: plan.ToRead.Count(r => r.Existing is not null),
            Updated: plan.Updated.Count,
            Removed: plan.Removed.Count,
            Unchanged: plan.Unchanged,
            ReadProblems: problems,
            scan.Duplicates,
            stopwatch.Elapsed);
    }

    private async Task<IReadOnlyList<Song>> ReadAndSaveAsync(
        IReadOnlyList<PendingRead> toRead,
        SyncOptions options,
        IProgress<SyncProgress>? progress,
        CancellationToken ct)
    {
        progress?.Report(new SyncProgress(0, toRead.Count, null));
        if (toRead.Count == 0)
            return [];

        var channel = Channel.CreateUnbounded<Song>(new UnboundedChannelOptions { SingleReader = true });

        var producer = Task.Run(async () =>
        {
            try
            {
                var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallelism), CancellationToken = ct };
                await Parallel.ForEachAsync(toRead, parallel, (item, _) =>
                {
                    channel.Writer.TryWrite(BuildSong(item, ReadSafely(item.Pak.FullPath)));
                    return ValueTask.CompletedTask;
                });
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, CancellationToken.None);

        // Drain everything that was read, even after a cancellation, so finished work is not lost.
        var batch = new List<Song>(options.BatchSize);
        var processed = 0;
        var problems = new List<Song>();
        try
        {
            await foreach (var song in channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                batch.Add(song);
                if (song.ReadStatus != ReadStatus.Ok)
                    problems.Add(song);
                progress?.Report(new SyncProgress(++processed, toRead.Count, song.FileName));

                if (batch.Count >= options.BatchSize)
                {
                    await repository.SaveScanResultsAsync(batch, CancellationToken.None);
                    batch.Clear();
                }
            }
        }
        finally
        {
            if (batch.Count > 0)
                await repository.SaveScanResultsAsync(batch, CancellationToken.None);
            await producer;
        }

        return problems;
    }

    private SongReadResult ReadSafely(string pakPath)
    {
        try
        {
            return reader.Read(pakPath);
        }
        catch (Exception ex)
        {
            return SongReadResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Song BuildSong(PendingRead item, SongReadResult result)
    {
        var song = item.Existing ?? new Song
        {
            FileName = item.Pak.FileName,
            Author = result.Source == MetadataSource.PatchedDlcCatalog ? PatchedDlcAuthor : "",
        };
        return song.WithFileState(item.Pak) with
        {
            Metadata = result.Metadata,
            ReadStatus = result.Status,
            ReadError = result.Error,
            FileSize = item.Pak.Size,
            FileModifiedUtc = item.Pak.ModifiedUtc,
        };
    }
}

internal sealed record PendingRead(PakFile Pak, Song? Existing);

internal sealed record SyncPlan(
    IReadOnlyList<PendingRead> ToRead,
    IReadOnlyList<Song> Updated,
    IReadOnlyList<string> Removed,
    int Unchanged)
{
    public static SyncPlan Create(LibraryScan scan, IReadOnlyDictionary<string, Song> existing, bool forceReread)
    {
        var toRead = new List<PendingRead>();
        var updated = new List<Song>();
        var unchanged = 0;

        foreach (var pak in scan.Paks)
        {
            if (!existing.TryGetValue(pak.FileName, out var song))
            {
                toRead.Add(new PendingRead(pak, null));
            }
            else if (forceReread || song.FileSize != pak.Size || song.FileModifiedUtc != pak.ModifiedUtc)
            {
                // Also covers rows imported from v1, which have no size or date yet.
                toRead.Add(new PendingRead(pak, song));
            }
            else if (song.Location != pak.Location || song.RelativePath != pak.RelativePath || song.HasSignature != pak.HasSignature)
            {
                // Moved by hand or .sig added/removed: same content, no need to open the pak (RF-BIB-10).
                updated.Add(song.WithFileState(pak));
            }
            else
            {
                unchanged++;
            }
        }

        var onDisk = scan.Paks.Select(p => p.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = existing.Keys.Where(name => !onDisk.Contains(name)).ToList();

        return new SyncPlan(toRead, updated, removed, unchanged);
    }
}

internal static class SongFileStateExtensions
{
    public static Song WithFileState(this Song song, PakFile pak) => song with
    {
        Location = pak.Location,
        RelativePath = pak.RelativePath,
        HasSignature = pak.HasSignature,
    };
}
