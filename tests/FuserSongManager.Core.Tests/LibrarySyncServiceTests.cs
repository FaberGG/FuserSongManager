using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;

namespace FuserSongManager.Core.Tests;

public class LibrarySyncServiceTests
{
    private static readonly LibraryPaths Paths = LibraryPaths.FromFuserDirectory(@"C:\Fuser");

    private readonly FakeScanner _scanner = new();
    private readonly FakeReader _reader = new();
    private readonly InMemoryRepository _repository = new();
    private readonly LibrarySyncService _service;

    public LibrarySyncServiceTests() => _service = new LibrarySyncService(_scanner, _reader, _repository);

    [Fact]
    public async Task New_paks_are_read_and_added()
    {
        _scanner.Add("A_P");
        _scanner.Add("B_P", SongLocation.Disabled, subfolder: "pack");

        var result = await _service.SyncAsync(Paths);

        Assert.Equal(2, result.Added);
        Assert.Equal(2, _reader.ReadPaths.Count);
        var b = _repository.Songs["B_P"];
        Assert.Equal("Title of B_P", b.Metadata.Title);
        Assert.Equal(SongLocation.Disabled, b.Location);
        Assert.Equal(Path.Combine("pack", "B_P.pak"), b.RelativePath);
        Assert.Equal(1000, b.FileSize);
    }

    [Fact]
    public async Task Second_sync_without_changes_reads_no_pak()
    {
        _scanner.Add("A_P");
        _scanner.Add("B_P");
        await _service.SyncAsync(Paths);
        _reader.ReadPaths.Clear();

        var result = await _service.SyncAsync(Paths);

        Assert.Empty(_reader.ReadPaths);
        Assert.Equal(2, result.Unchanged);
        Assert.Equal(0, result.PaksRead);
    }

    [Fact]
    public async Task Songs_saved_without_instruments_are_read_once_more()
    {
        _scanner.Add("A_P");
        _scanner.Add("B_P");
        _scanner.Add("Broken_P");
        _reader.Failing.Add("Broken_P");
        await _service.SyncAsync(Paths);
        // As left by a version that did not store the instruments.
        foreach (var name in new[] { "A_P", "B_P" })
            _repository.Songs[name] = _repository.Songs[name] with { Instruments = null };
        _repository.Songs["Broken_P"] = _repository.Songs["Broken_P"] with { Instruments = null };
        _reader.ReadPaths.Clear();

        var result = await _service.SyncAsync(Paths);

        Assert.Equal(2, result.Reread);   // a failed read is not retried for this
        Assert.All(new[] { "A_P", "B_P" }, name => Assert.NotNull(_repository.Songs[name].Instruments));
        _reader.ReadPaths.Clear();
        Assert.Equal(0, (await _service.SyncAsync(Paths)).PaksRead);
    }

    [Fact]
    public async Task Modified_pak_is_reread_and_keeps_user_data()
    {
        var pak = _scanner.Add("A_P");
        await _service.SyncAsync(Paths);
        await _repository.UpdateUserDataAsync("A_P", 4, "nota", "autor");
        _scanner.Replace(pak, pak with { Size = 2000 });
        _reader.ReadPaths.Clear();

        var result = await _service.SyncAsync(Paths);

        Assert.Equal(1, result.Reread);
        Assert.Single(_reader.ReadPaths);
        var song = _repository.Songs["A_P"];
        Assert.Equal(2000, song.FileSize);
        Assert.Equal((4, "nota", "autor"), (song.Rating, song.Notes, song.Author));
    }

    [Fact]
    public async Task Pak_moved_by_hand_updates_location_without_reading_and_keeps_user_data()
    {
        var pak = _scanner.Add("A_P");
        await _service.SyncAsync(Paths);
        await _repository.UpdateUserDataAsync("A_P", 5, "favorita", "");
        _scanner.Replace(pak, pak with { Location = SongLocation.Disabled, RelativePath = Path.Combine("sub", "A_P.pak") });
        _reader.ReadPaths.Clear();

        var result = await _service.SyncAsync(Paths);

        Assert.Empty(_reader.ReadPaths);
        Assert.Equal(1, result.Updated);
        var song = _repository.Songs["A_P"];
        Assert.False(song.IsEnabled);
        Assert.Equal(Path.Combine("sub", "A_P.pak"), song.RelativePath);
        Assert.Equal(5, song.Rating);
    }

    [Fact]
    public async Task Missing_signature_is_recorded_without_reading()
    {
        var pak = _scanner.Add("A_P");
        await _service.SyncAsync(Paths);
        _scanner.Replace(pak, pak with { HasSignature = false });
        _reader.ReadPaths.Clear();

        await _service.SyncAsync(Paths);

        Assert.Empty(_reader.ReadPaths);
        Assert.False(_repository.Songs["A_P"].HasSignature);
    }

    [Fact]
    public async Task Paks_gone_from_disk_are_removed()
    {
        var pak = _scanner.Add("A_P");
        _scanner.Add("B_P");
        await _service.SyncAsync(Paths);
        _scanner.Paks.Remove(pak);

        var result = await _service.SyncAsync(Paths);

        Assert.Equal(1, result.Removed);
        Assert.False(_repository.Songs.ContainsKey("A_P"));
        Assert.True(_repository.Songs.ContainsKey("B_P"));
    }

    [Fact]
    public async Task Unreadable_paks_are_kept_as_failed_and_do_not_stop_the_sync()
    {
        _scanner.Add("Bad_P");
        _scanner.Add("Throws_P");
        _scanner.Add("Good_P");
        _reader.Failing.Add("Bad_P");
        _reader.Throwing.Add("Throws_P");

        var result = await _service.SyncAsync(Paths);

        Assert.Equal(3, result.Added);
        Assert.Equal(2, result.Failed);
        Assert.Equal(ReadStatus.Failed, _repository.Songs["Bad_P"].ReadStatus);
        Assert.Equal(ReadStatus.Failed, _repository.Songs["Throws_P"].ReadStatus);
        Assert.Contains("boom", _repository.Songs["Throws_P"].ReadError);
        Assert.Equal(ReadStatus.Ok, _repository.Songs["Good_P"].ReadStatus);
    }

    [Fact]
    public async Task Rows_imported_from_v1_are_read_once_keeping_user_data()
    {
        _repository.Songs["A_P"] = new Song
        {
            FileName = "A_P",
            Metadata = new SongMetadata("Unknown", "Unknown", 0, Genre.Classical, null, null, 0),
            Location = SongLocation.Enabled,
            Rating = 3,
            Notes = "from v1",
        };
        _scanner.Add("A_P");

        var first = await _service.SyncAsync(Paths);
        var second = await _service.SyncAsync(Paths);

        Assert.Equal(1, first.Reread);
        Assert.Equal(0, second.PaksRead);
        var song = _repository.Songs["A_P"];
        Assert.Equal("Title of A_P", song.Metadata.Title);
        Assert.Equal((3, "from v1"), (song.Rating, song.Notes));
    }

    [Fact]
    public async Task Force_rereads_every_pak()
    {
        _scanner.Add("A_P");
        _scanner.Add("B_P");
        await _service.SyncAsync(Paths);
        _reader.ReadPaths.Clear();

        var result = await _service.SyncAsync(Paths, new SyncOptions { ForceReread = true });

        Assert.Equal(2, result.Reread);
        Assert.Equal(2, _reader.ReadPaths.Count);
    }

    [Fact]
    public async Task Progress_reaches_the_total_and_results_are_saved_in_batches()
    {
        for (var i = 0; i < 25; i++)
            _scanner.Add($"S{i:00}_P");
        var reports = new List<SyncProgress>();

        await _service.SyncAsync(Paths, new SyncOptions { BatchSize = 10, Parallelism = 4 }, new SynchronousProgress<SyncProgress>(reports.Add));

        Assert.Equal(25, _repository.Songs.Count);
        Assert.Equal(3, _repository.SaveCalls);
        Assert.Equal(new SyncProgress(0, 25, null), reports[0]);
        Assert.Equal(25, reports[^1].Processed);
    }

    [Fact]
    public async Task Cancelled_sync_keeps_what_was_already_read()
    {
        for (var i = 0; i < 50; i++)
            _scanner.Add($"S{i:00}_P");
        _reader.Delay = TimeSpan.FromMilliseconds(5);
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<SyncProgress>(p =>
        {
            if (p.Processed == 5)
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.SyncAsync(Paths, new SyncOptions { Parallelism = 1, BatchSize = 100 }, progress, cts.Token));

        Assert.InRange(_repository.Songs.Count, 5, 49);
    }

    [Fact]
    public async Task Patched_dlc_songs_get_harmonix_as_author_on_insert_only()
    {
        var reader = new CatalogReader();
        var service = new LibrarySyncService(_scanner, reader, _repository);
        _scanner.Add("adoreyou_P");

        await service.SyncAsync(Paths);

        Assert.Equal(LibrarySyncService.PatchedDlcAuthor, _repository.Songs["adoreyou_P"].Author);
    }

    private sealed class CatalogReader : Abstractions.ISongMetadataReader
    {
        public SongReadResult Read(string pakPath) =>
            new(SongMetadata.Empty with { Title = "Adore You" }, ReadStatus.Ok, null, MetadataSource.PatchedDlcCatalog);
    }

    /// <summary>Progress&lt;T&gt; posts to the thread pool; tests need the callbacks to run inline.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
