using Microsoft.Data.Sqlite;

namespace FuserSongManager.Infrastructure.Data;

/// <summary>Opens connections to the manager database and applies schema migrations.</summary>
public sealed class SqliteDatabase
{
    // Each migration runs once, in order, inside a transaction. Never edit one that has shipped; add a new one.
    private static readonly (int Version, string Sql)[] Migrations =
    [
        (1, """
            CREATE TABLE songs (
                filename          TEXT PRIMARY KEY COLLATE NOCASE,
                song_name         TEXT,
                artist            TEXT,
                year              INTEGER,
                genre             INTEGER,
                key               INTEGER,
                mode              INTEGER,
                bpm               INTEGER,
                enabled           INTEGER NOT NULL,
                rating            INTEGER NOT NULL DEFAULT 0,
                notes             TEXT NOT NULL DEFAULT '',
                author            TEXT NOT NULL DEFAULT '',
                relative_path     TEXT,
                has_signature     INTEGER NOT NULL DEFAULT 0,
                file_size         INTEGER,
                file_modified_utc TEXT,
                read_status       TEXT NOT NULL DEFAULT 'ok',
                read_error        TEXT,
                extra_json        TEXT
            );
            """),
    ];

    public SqliteDatabase(string path)
    {
        Path = path;
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
    }

    public string Path { get; }

    public string ConnectionString { get; }

    public static int LatestVersion => Migrations[^1].Version;

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        // WAL lets the UI keep reading while a scan writes.
        Execute(connection, "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;");
        return connection;
    }

    /// <summary>Creates the file if needed and applies pending migrations. Returns true if the database was just created.</summary>
    public bool Migrate()
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = Open();
        Execute(connection, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");
        var current = CurrentVersion(connection);

        foreach (var (version, sql) in Migrations.Where(m => m.Version > current))
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, sql, transaction);
            Execute(connection, "DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES ($v);", transaction, ("$v", version));
            transaction.Commit();
        }

        return current == 0;
    }

    public static int CurrentVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
