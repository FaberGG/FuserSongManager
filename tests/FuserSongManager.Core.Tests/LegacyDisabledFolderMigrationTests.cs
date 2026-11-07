using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;

namespace FuserSongManager.Core.Tests;

public sealed class LegacyDisabledFolderMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fsm-mig-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private LibraryPaths Paths => LibraryPaths.FromFuserDirectory(_root);

    private string Legacy => Path.Combine(Paths.PaksDirectory, "disabled_songs");

    private void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void Moves_files_and_subfolders_and_removes_the_legacy_folder()
    {
        Write(Path.Combine(Legacy, "a_P.pak"));
        Write(Path.Combine(Legacy, "sub", "b_P.pak"));

        var moved = LegacyDisabledFolderMigration.Run(Paths);

        Assert.Equal(2, moved);
        Assert.True(File.Exists(Path.Combine(Paths.DisabledDirectory, "a_P.pak")));
        Assert.True(File.Exists(Path.Combine(Paths.DisabledDirectory, "sub", "b_P.pak")));
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void Does_not_overwrite_existing_files()
    {
        Write(Path.Combine(Legacy, "a_P.pak"));
        Write(Path.Combine(Paths.DisabledDirectory, "a_P.pak"));

        var moved = LegacyDisabledFolderMigration.Run(Paths);

        Assert.Equal(0, moved);
        Assert.True(File.Exists(Path.Combine(Legacy, "a_P.pak")));
    }

    [Fact]
    public void Does_nothing_without_a_legacy_folder()
    {
        Assert.Equal(0, LegacyDisabledFolderMigration.Run(Paths));
    }
}
