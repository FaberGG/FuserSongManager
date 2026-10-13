using FuserSongManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace FuserSongManager.Infrastructure.Data;

/// <summary>Settings found in the v1 <c>config.ini</c>.</summary>
public sealed record V1Config(string? FuserDirectory, string? DatabasePath, LauncherType LauncherType, string? ExecutablePath);

/// <summary>A v1 install: its config (if any) and the database it points to.</summary>
public sealed record V1Installation(string DatabasePath, V1Config? Config);

/// <summary>
/// Reads the v1 (Python) manager's config and database. Only reads: the v1 files are never modified,
/// so the user can go back to v1 at any time.
/// </summary>
public static class V1Import
{
    /// <summary><c>%LocalAppData%\FuserCustomSongManager</c>, where v1 keeps <c>config.ini</c> and <c>db\songs.sqlite</c>.</summary>
    public static string DefaultAppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuserCustomSongManager");

    public static V1Installation? Detect(string? appDataDirectory = null)
    {
        appDataDirectory ??= DefaultAppDataDirectory;
        var configPath = Path.Combine(appDataDirectory, "config.ini");
        var config = File.Exists(configPath) ? ReadConfig(configPath) : null;

        var candidates = new[] { config?.DatabasePath, Path.Combine(appDataDirectory, "db", "songs.sqlite") };
        var databasePath = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
        return databasePath is null ? null : new V1Installation(databasePath, config);
    }

    public static V1Config ReadConfig(string configPath)
    {
        // configparser format: "[main]" then "key = value" lines.
        var values = File.ReadAllLines(configPath)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        string? Get(string key) => values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

        return new V1Config(
            Get("fuser_path"),
            Get("database_path"),
            Enum.TryParse<LauncherType>(Get("install_type"), ignoreCase: true, out var launcher) ? launcher : LauncherType.Steam,
            Get("executable_path"));
    }

    public static AppSettings ToSettings(V1Config config) => new()
    {
        FuserDirectory = config.FuserDirectory,
        LauncherType = config.LauncherType,
        ExecutablePath = config.ExecutablePath,
    };

    /// <summary>
    /// Reads every v1 row. Rows come back without file size or date, so the first sync re-reads
    /// their paks with v2 while keeping rating, notes and author.
    /// </summary>
    public static IReadOnlyList<Song> ReadSongs(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        // Column names as created by v1 loader.init_database (the title column is "name").
        command.CommandText = "SELECT filename, name, artist, year, genre, key, mode, bpm, enabled, rating, notes, author FROM songs;";
        using var reader = command.ExecuteReader();

        var songs = new List<Song>();
        while (reader.Read())
        {
            var fileName = Text(reader, 0);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            songs.Add(new Song
            {
                FileName = fileName,
                Metadata = new SongMetadata(
                    Title: Text(reader, 1),
                    Artist: Text(reader, 2),
                    Year: Int(reader, 3),
                    Genre: Defined<Genre>(Int(reader, 4)),
                    // v1 stores "no value" as SongKey.Num = 12 and SongMode.Num = 2.
                    Key: Defined<MusicalKey>(Int(reader, 5)),
                    Mode: Defined<SongMode>(Int(reader, 6)),
                    Bpm: Int(reader, 7)),
                Location = Int(reader, 8) == 1 ? SongLocation.Enabled : SongLocation.Disabled,
                Rating = Math.Clamp(Int(reader, 9) ?? 0, 0, 5),
                Notes = Text(reader, 10) ?? "",
                Author = Text(reader, 11) ?? "",
            });
        }
        return songs;
    }

    // v1 inserted every value as a quoted string, so do not trust the stored type.
    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));

    private static int? Int(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : int.TryParse(Convert.ToString(r.GetValue(i)), out var value) ? value : null;

    private static T? Defined<T>(int? value) where T : struct, Enum =>
        value is { } v && Enum.IsDefined(typeof(T), v) ? (T)(object)v : null;
}
