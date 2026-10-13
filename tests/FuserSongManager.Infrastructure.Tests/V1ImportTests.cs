using System.Security.Cryptography;
using FuserSongManager.Core.Models;
using FuserSongManager.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace FuserSongManager.Infrastructure.Tests;

public sealed class V1ImportTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>Creates a database exactly like v1 loader.init_database / insert_song_into_db (every value quoted).</summary>
    private string CreateV1Database(string path, params string[] valueRows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS songs (
            filename TEXT PRIMARY KEY, name TEXT, artist TEXT, year INTEGER, genre INTEGER, key INTEGER,
            mode INTEGER, bpm INTEGER, enabled INTEGER, rating INTEGER, notes TEXT, author TEXT);
            """ + string.Concat(valueRows.Select(v => $"INSERT INTO songs VALUES ({v});"));
        command.ExecuteNonQuery();
        return path;
    }

    [Fact]
    public void Reads_v1_rows_mapping_columns_and_no_value_enums()
    {
        var path = CreateV1Database(_temp.File("songs.sqlite"),
            "'AW_Run_ShpQn_P','Run','AWOLNATION','2015','2','8','1','138','0','4','mi nota','ShpQn'",
            "'Dont''Stop_P','Don''t Stop','Fleetwood Mac','1977','4','12','2','0','1','0','',''");

        var songs = V1Import.ReadSongs(path).OrderBy(s => s.FileName).ToList();

        var run = songs.Single(s => s.FileName == "AW_Run_ShpQn_P");
        Assert.Equal(new SongMetadata("Run", "AWOLNATION", 2015, Genre.Rock, MusicalKey.Ab, SongMode.Minor, 138), run.Metadata);
        Assert.Equal(SongLocation.Disabled, run.Location);
        Assert.Equal((4, "mi nota", "ShpQn"), (run.Rating, run.Notes, run.Author));
        // No file state: the first sync re-reads the pak.
        Assert.Null(run.FileSize);
        Assert.Null(run.RelativePath);

        var dont = songs.Single(s => s.FileName == "Dont'Stop_P");
        Assert.Equal("Don't Stop", dont.Metadata.Title);
        Assert.Null(dont.Metadata.Key);   // v1 SongKey.Num = 12
        Assert.Null(dont.Metadata.Mode);  // v1 SongMode.Num = 2
        Assert.True(dont.IsEnabled);
    }

    [Fact]
    public void Never_modifies_the_v1_database()
    {
        var path = CreateV1Database(_temp.File("songs.sqlite"), "'A_P','A','B','2000','1','1','1','100','1','3','',''");
        SqliteConnection.ClearAllPools();
        var before = SHA256.HashData(File.ReadAllBytes(path));

        V1Import.ReadSongs(path);
        SqliteConnection.ClearAllPools();

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Imported_rows_land_in_the_v2_database_with_user_data()
    {
        var v1 = CreateV1Database(_temp.File("v1.sqlite"), "'A_P','A','B','2000','1','1','1','100','1','3','note','me'");
        var database = new SqliteDatabase(_temp.File("v2.sqlite"));
        database.Migrate();
        var repository = new SqliteSongRepository(database);

        await repository.InsertIfMissingAsync(V1Import.ReadSongs(v1));

        var song = Assert.Single(await repository.GetAllAsync());
        Assert.Equal((3, "note", "me"), (song.Rating, song.Notes, song.Author));
    }

    [Fact]
    public void Detect_uses_the_database_path_from_config_ini()
    {
        var customDb = CreateV1Database(_temp.File(Path.Combine("elsewhere", "my.sqlite")));
        File.WriteAllText(_temp.File("config.ini"), $"""
            [main]
            fuser_path = C:\Program Files (x86)\Steam\steamapps\common\Fuser
            database_path = {customDb}
            install_type = Local
            executable_path = C:\Games\Fuser\start.bat

            """);

        var installation = V1Import.Detect(_temp.Path);

        Assert.NotNull(installation);
        Assert.Equal(customDb, installation.DatabasePath);
        Assert.Equal(new V1Config(@"C:\Program Files (x86)\Steam\steamapps\common\Fuser", customDb, LauncherType.Local, @"C:\Games\Fuser\start.bat"), installation.Config);
        var settings = V1Import.ToSettings(installation.Config!);
        Assert.Equal(LauncherType.Local, settings.LauncherType);
        Assert.Equal(@"C:\Games\Fuser\start.bat", settings.ExecutablePath);
    }

    [Fact]
    public void Detect_falls_back_to_the_default_database_location()
    {
        var path = CreateV1Database(_temp.File(Path.Combine("db", "songs.sqlite")));

        Assert.Equal(path, V1Import.Detect(_temp.Path)?.DatabasePath);
    }

    [Fact]
    public void Detect_returns_null_without_a_v1_install()
    {
        Assert.Null(V1Import.Detect(_temp.Path));
    }
}
