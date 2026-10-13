using System.Globalization;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace FuserSongManager.Infrastructure.Data;

public sealed class SqliteSongRepository(SqliteDatabase database) : ISongRepository
{
    private const string Columns = """
        filename, song_name, artist, year, genre, key, mode, bpm, enabled, rating, notes, author,
        relative_path, has_signature, file_size, file_modified_utc, read_status, read_error
        """;

    private const string Values = """
        $filename, $song_name, $artist, $year, $genre, $key, $mode, $bpm, $enabled, $rating, $notes, $author,
        $relative_path, $has_signature, $file_size, $file_modified_utc, $read_status, $read_error
        """;

    // On conflict only game metadata and file state change: rating, notes and author belong to the user.
    private const string UpsertScanSql = $"""
        INSERT INTO songs ({Columns}) VALUES ({Values})
        ON CONFLICT(filename) DO UPDATE SET
            song_name = excluded.song_name, artist = excluded.artist, year = excluded.year,
            genre = excluded.genre, key = excluded.key, mode = excluded.mode, bpm = excluded.bpm,
            enabled = excluded.enabled, relative_path = excluded.relative_path,
            has_signature = excluded.has_signature, file_size = excluded.file_size,
            file_modified_utc = excluded.file_modified_utc, read_status = excluded.read_status,
            read_error = excluded.read_error;
        """;

    private const string InsertIfMissingSql = $"INSERT OR IGNORE INTO songs ({Columns}) VALUES ({Values});";

    public Task<IReadOnlyList<Song>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM songs ORDER BY filename;";
        using var reader = command.ExecuteReader();

        var songs = new List<Song>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            songs.Add(ReadSong(reader));
        }
        return Task.FromResult<IReadOnlyList<Song>>(songs);
    }

    public Task SaveScanResultsAsync(IReadOnlyCollection<Song> songs, CancellationToken ct = default) =>
        WriteAll(songs, UpsertScanSql, ct);

    /// <summary>Inserts complete rows (including user data) and skips songs that already exist. Used by the v1 import.</summary>
    public Task InsertIfMissingAsync(IReadOnlyCollection<Song> songs, CancellationToken ct = default) =>
        WriteAll(songs, InsertIfMissingSql, ct);

    public Task DeleteAsync(IReadOnlyCollection<string> fileNames, CancellationToken ct = default)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM songs WHERE filename = $filename;";
        var parameter = command.Parameters.Add("$filename", SqliteType.Text);
        foreach (var fileName in fileNames)
        {
            ct.ThrowIfCancellationRequested();
            parameter.Value = fileName;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task UpdateUserDataAsync(string fileName, int rating, string notes, string author, CancellationToken ct = default)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE songs SET rating = $rating, notes = $notes, author = $author WHERE filename = $filename;";
        command.Parameters.AddWithValue("$rating", Math.Clamp(rating, 0, 5));
        command.Parameters.AddWithValue("$notes", notes);
        command.Parameters.AddWithValue("$author", author);
        command.Parameters.AddWithValue("$filename", fileName);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private Task WriteAll(IReadOnlyCollection<Song> songs, string sql, CancellationToken ct)
    {
        if (songs.Count == 0)
            return Task.CompletedTask;

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var song in songs)
        {
            ct.ThrowIfCancellationRequested();
            Bind(command, song);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return Task.CompletedTask;
    }

    private static void Bind(SqliteCommand command, Song song)
    {
        command.Parameters.Clear();
        var m = song.Metadata;
        command.Parameters.AddWithValue("$filename", song.FileName);
        command.Parameters.AddWithValue("$song_name", Db(m.Title));
        command.Parameters.AddWithValue("$artist", Db(m.Artist));
        command.Parameters.AddWithValue("$year", Db(m.Year));
        command.Parameters.AddWithValue("$genre", Db((int?)m.Genre));
        command.Parameters.AddWithValue("$key", Db((int?)m.Key));
        command.Parameters.AddWithValue("$mode", Db((int?)m.Mode));
        command.Parameters.AddWithValue("$bpm", Db(m.Bpm));
        command.Parameters.AddWithValue("$enabled", song.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$rating", Math.Clamp(song.Rating, 0, 5));
        command.Parameters.AddWithValue("$notes", song.Notes);
        command.Parameters.AddWithValue("$author", song.Author);
        command.Parameters.AddWithValue("$relative_path", Db(song.RelativePath));
        command.Parameters.AddWithValue("$has_signature", song.HasSignature ? 1 : 0);
        command.Parameters.AddWithValue("$file_size", Db(song.FileSize));
        command.Parameters.AddWithValue("$file_modified_utc", Db(song.FileModifiedUtc?.ToString("O", CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$read_status", ReadStatusToDb(song.ReadStatus));
        command.Parameters.AddWithValue("$read_error", Db(song.ReadError));
    }

    private static Song ReadSong(SqliteDataReader r) => new()
    {
        FileName = r.GetString(0),
        Metadata = new SongMetadata(
            Title: NullableString(r, 1),
            Artist: NullableString(r, 2),
            Year: NullableInt(r, 3),
            Genre: (Genre?)NullableInt(r, 4),
            Key: (MusicalKey?)NullableInt(r, 5),
            Mode: (SongMode?)NullableInt(r, 6),
            Bpm: NullableInt(r, 7)),
        Location = r.GetInt64(8) == 1 ? SongLocation.Enabled : SongLocation.Disabled,
        Rating = (int)r.GetInt64(9),
        Notes = r.GetString(10),
        Author = r.GetString(11),
        RelativePath = NullableString(r, 12),
        HasSignature = r.GetInt64(13) == 1,
        FileSize = r.IsDBNull(14) ? null : r.GetInt64(14),
        FileModifiedUtc = r.IsDBNull(15) ? null : DateTime.Parse(r.GetString(15), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ReadStatus = ReadStatusFromDb(r.GetString(16)),
        ReadError = NullableString(r, 17),
    };

    private static string ReadStatusToDb(ReadStatus status) => status switch
    {
        ReadStatus.Ok => "ok",
        ReadStatus.Partial => "partial",
        _ => "failed",
    };

    private static ReadStatus ReadStatusFromDb(string value) => value switch
    {
        "ok" => ReadStatus.Ok,
        "partial" => ReadStatus.Partial,
        _ => ReadStatus.Failed,
    };

    private static object Db<T>(T? value) => value is null ? DBNull.Value : value;

    private static string? NullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static int? NullableInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : (int)r.GetInt64(i);
}
