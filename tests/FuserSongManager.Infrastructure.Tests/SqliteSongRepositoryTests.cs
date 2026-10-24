using FuserSongManager.Core.Models;
using FuserSongManager.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace FuserSongManager.Infrastructure.Tests;

public sealed class SqliteSongRepositoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SqliteDatabase _database;
    private readonly SqliteSongRepository _repository;

    public SqliteSongRepositoryTests()
    {
        _database = new SqliteDatabase(_temp.File("songs.sqlite"));
        _database.Migrate();
        _repository = new SqliteSongRepository(_database);
    }

    public void Dispose() => _temp.Dispose();

    private static Song FullSong(string fileName = "A_P") => new()
    {
        FileName = fileName,
        Metadata = new SongMetadata("Run", "AWOLNATION", 2015, Genre.Rock, MusicalKey.Ab, SongMode.Minor, 138),
        ReadStatus = ReadStatus.Partial,
        ReadError = "Missing: Mode",
        Location = SongLocation.Disabled,
        RelativePath = Path.Combine("Pack", "A_P.pak"),
        HasSignature = true,
        FileSize = 6_771_200,
        FileModifiedUtc = new DateTime(2023, 11, 23, 10, 20, 30, DateTimeKind.Utc).AddTicks(1234567),
        Rating = 4,
        Notes = "nota",
        Author = "ShpQn",
        Instruments = new SongInstruments("Drums", "Synth", null, "Vocal"),
    };

    [Fact]
    public void Migrate_creates_the_schema_once()
    {
        var database = new SqliteDatabase(_temp.File("other.sqlite"));

        Assert.True(database.Migrate());
        Assert.False(database.Migrate());
        using var connection = database.Open();
        Assert.Equal(SqliteDatabase.LatestVersion, SqliteDatabase.CurrentVersion(connection));
    }

    [Fact]
    public async Task Saved_song_round_trips_every_field()
    {
        var song = FullSong();

        await _repository.InsertIfMissingAsync([song]);

        Assert.Equal(song, Assert.Single(await _repository.GetAllAsync()));
    }

    [Fact]
    public async Task Instruments_are_updated_by_a_scan_and_unreadable_extras_are_dropped()
    {
        await _repository.SaveScanResultsAsync([FullSong() with { Instruments = null }]);
        Assert.Null(Assert.Single(await _repository.GetAllAsync()).Instruments);

        var instruments = new SongInstruments("Sampler", "Guitar", "Horns", "Vocal");
        await _repository.SaveScanResultsAsync([FullSong() with { Instruments = instruments }]);
        Assert.Equal(instruments, Assert.Single(await _repository.GetAllAsync()).Instruments);

        using (var connection = new SqliteDatabase(_temp.File("songs.sqlite")).Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE songs SET extra_json = '{not json';";
            command.ExecuteNonQuery();
        }
        // The next sync reads the pak again because the instruments are missing.
        Assert.Null(Assert.Single(await _repository.GetAllAsync()).Instruments);
    }

    [Fact]
    public async Task Unknown_values_round_trip_as_null()
    {
        var song = new Song { FileName = "Empty_P", ReadStatus = ReadStatus.Failed, ReadError = "boom" };

        await _repository.SaveScanResultsAsync([song]);

        Assert.Equal(song, Assert.Single(await _repository.GetAllAsync()));
    }

    [Fact]
    public async Task Scan_results_never_overwrite_user_data()
    {
        await _repository.InsertIfMissingAsync([FullSong()]);
        var rescanned = FullSong() with
        {
            Metadata = SongMetadata.Empty with { Title = "New title" },
            Location = SongLocation.Enabled,
            Rating = 0,
            Notes = "",
            Author = "",
        };

        await _repository.SaveScanResultsAsync([rescanned]);

        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("New title", stored.Metadata.Title);
        Assert.True(stored.IsEnabled);
        Assert.Equal((4, "nota", "ShpQn"), (stored.Rating, stored.Notes, stored.Author));
    }

    [Fact]
    public async Task Insert_if_missing_keeps_existing_rows()
    {
        await _repository.InsertIfMissingAsync([FullSong()]);

        await _repository.InsertIfMissingAsync([FullSong() with { Notes = "other", Rating = 1 }]);

        Assert.Equal("nota", Assert.Single(await _repository.GetAllAsync()).Notes);
    }

    [Fact]
    public async Task File_names_are_case_insensitive_keys()
    {
        await _repository.SaveScanResultsAsync([FullSong("Song_P")]);

        await _repository.SaveScanResultsAsync([FullSong("SONG_P")]);

        Assert.Single(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task Quotes_in_names_and_notes_are_stored_safely()
    {
        // v1 built SQL with f-strings and broke on these.
        var song = FullSong("Don't_Stop_P") with { Notes = "it's '; DROP TABLE songs; --" };

        await _repository.SaveScanResultsAsync([song]);
        await _repository.UpdateUserDataAsync("Don't_Stop_P", 5, song.Notes, "O'Brien");
        await _repository.DeleteAsync(["Nope'_P"]);

        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("Don't_Stop_P", stored.FileName);
        Assert.Equal("O'Brien", stored.Author);
    }

    [Fact]
    public async Task Delete_and_update_user_data()
    {
        await _repository.SaveScanResultsAsync([FullSong("A_P"), FullSong("B_P")]);

        await _repository.DeleteAsync(["A_P"]);
        await _repository.UpdateUserDataAsync("B_P", 9, "n", "a");

        var stored = Assert.Single(await _repository.GetAllAsync());
        Assert.Equal("B_P", stored.FileName);
        Assert.Equal(5, stored.Rating); // clamped to 0–5
    }

    [Fact]
    public async Task Status_is_stored_as_readable_text()
    {
        await _repository.SaveScanResultsAsync([FullSong()]);

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT read_status, enabled, genre, key, mode FROM songs";
        using var reader = command.ExecuteReader();
        reader.Read();
        Assert.Equal("partial", reader.GetString(0));
        Assert.Equal((0L, 2L, 8L, 1L), (reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)));
    }
}
