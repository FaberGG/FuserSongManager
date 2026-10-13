using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;

namespace FuserSongManager.Core.Tests;

public class SongStateServiceTests
{
    private static readonly LibraryPaths Paths = LibraryPaths.FromFuserDirectory(@"C:\Fuser");

    private readonly FakeFileService _files = new();
    private readonly InMemoryRepository _repository = new();
    private readonly FakeGame _game = new();
    private readonly SongStateService _service;

    public SongStateServiceTests() => _service = new SongStateService(_files, _repository, _game);

    private Song Stored(string fileName, SongLocation location, string? relativePath = "x.pak", int rating = 0)
    {
        var song = new Song { FileName = fileName, Location = location, RelativePath = relativePath, Rating = rating };
        _repository.Songs[fileName] = song;
        return song;
    }

    [Fact]
    public async Task Moves_songs_updates_the_library_and_clears_the_game_cache_once()
    {
        var a = Stored("A_P", SongLocation.Disabled, rating: 5);
        var b = Stored("B_P", SongLocation.Disabled);

        var results = await _service.SetLocationAsync([a, b], SongLocation.Enabled, Paths);

        Assert.All(results, r => Assert.Equal(StateChangeOutcome.Moved, r.Outcome));
        Assert.True(_repository.Songs["A_P"].IsEnabled);
        Assert.Equal(5, _repository.Songs["A_P"].Rating);
        Assert.Equal(1, _files.CacheClears);
    }

    [Fact]
    public async Task Nothing_is_touched_while_the_game_runs()
    {
        var a = Stored("A_P", SongLocation.Disabled);
        _game.Running = true;

        var results = await _service.SetLocationAsync([a], SongLocation.Enabled, Paths);

        Assert.Equal(StateChangeOutcome.GameRunning, Assert.Single(results).Outcome);
        Assert.Empty(_files.Moves);
        Assert.False(_repository.Songs["A_P"].IsEnabled);
    }

    [Fact]
    public async Task Songs_already_in_place_or_not_scanned_are_not_moved()
    {
        var already = Stored("A_P", SongLocation.Enabled);
        var notScanned = Stored("B_P", SongLocation.Disabled, relativePath: null);

        var results = await _service.SetLocationAsync([already, notScanned], SongLocation.Enabled, Paths);

        Assert.Equal([StateChangeOutcome.AlreadyThere, StateChangeOutcome.NotIndexed], results.Select(r => r.Outcome));
        Assert.Empty(_files.Moves);
        Assert.Equal(0, _files.CacheClears);
    }

    [Fact]
    public async Task Failed_moves_do_not_change_the_library_and_the_rest_continue()
    {
        var a = Stored("A_P", SongLocation.Disabled);
        var b = Stored("B_P", SongLocation.Disabled);
        _files.Outcomes["A_P"] = StateChangeOutcome.MissingSignature;

        var results = await _service.SetLocationAsync([a, b], SongLocation.Enabled, Paths);

        Assert.Equal([StateChangeOutcome.MissingSignature, StateChangeOutcome.Moved], results.Select(r => r.Outcome));
        Assert.False(_repository.Songs["A_P"].IsEnabled);
        Assert.True(_repository.Songs["B_P"].IsEnabled);
    }
}

public class SongImportServiceTests
{
    private static readonly LibraryPaths Paths = LibraryPaths.FromFuserDirectory(@"C:\Fuser");

    private readonly FakeImporter _importer = new();
    private readonly FakeGame _game = new();
    private readonly FakeFileService _files = new();
    private readonly FakeScanner _scanner = new();
    private readonly FakeReader _reader = new();
    private readonly SongImportService _service;

    public SongImportServiceTests() =>
        _service = new SongImportService(_importer, _game, _files, new LibrarySyncService(_scanner, _reader, new InMemoryRepository()));

    [Fact]
    public async Task Import_is_blocked_while_the_game_runs()
    {
        _game.Running = true;

        var (import, sync) = await _service.ImportAsync(["a.zip"], Paths);

        Assert.Equal(ImportIssueKind.GameRunning, Assert.Single(import.Issues).Kind);
        Assert.Null(sync);
        Assert.Equal(0, _importer.Calls);
    }

    [Fact]
    public async Task Successful_import_clears_the_cache_and_syncs_the_new_songs()
    {
        _importer.Result = new ImportResult([new ImportedSong("A_P", SongLocation.Enabled, "A_P.pak", false)], []);
        _scanner.Add("A_P");

        var (_, sync) = await _service.ImportAsync(["a.zip"], Paths);

        Assert.NotNull(sync);
        Assert.Equal(1, sync.Added);
        Assert.Equal(1, _files.CacheClears);
    }

    [Fact]
    public async Task Import_without_new_songs_skips_the_sync()
    {
        _importer.Result = new ImportResult([], [new ImportIssue("a.zip", null, ImportIssueKind.NoSongsFound, "none")]);

        var (_, sync) = await _service.ImportAsync(["a.zip"], Paths);

        Assert.Null(sync);
        Assert.Equal(0, _files.CacheClears);
    }
}

public class GameLaunchServiceTests
{
    private readonly FakeGame _game = new();
    private readonly FakeFileService _files = new();

    [Fact]
    public void Clears_the_cache_before_launching()
    {
        var result = new GameLaunchService(_game, _files).Launch(new AppSettings { FuserDirectory = @"C:\Fuser" });

        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.Equal(1, _files.CacheClears);
        Assert.Single(_game.Launches);
    }

    [Fact]
    public void Does_not_launch_twice_or_without_configuration()
    {
        var service = new GameLaunchService(_game, _files);

        Assert.Equal(LaunchOutcome.NotConfigured, service.Launch(new AppSettings()).Outcome);
        _game.Running = true;
        Assert.Equal(LaunchOutcome.AlreadyRunning, service.Launch(new AppSettings { FuserDirectory = @"C:\Fuser" }).Outcome);
        Assert.Empty(_game.Launches);
    }
}
