namespace FuserSongManager.Infrastructure.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsm-tests-" + Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        // SQLite pooling keeps files open; release them before deleting.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

/// <summary>
/// Runs only when <c>FSM_SAMPLE_PAKS</c> points to a folder of real song paks (kept out of the repository).
/// </summary>
public sealed class SamplePaksFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "FSM_SAMPLE_PAKS";

    public SamplePaksFactAttribute()
    {
        if (Directory is null)
            Skip = $"Set {EnvironmentVariable} to a folder with sample paks to run this test.";
    }

    public static string? Directory =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } dir && System.IO.Directory.Exists(dir) ? dir : null;
}
