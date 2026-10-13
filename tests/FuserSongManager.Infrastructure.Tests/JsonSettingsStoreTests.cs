using FuserSongManager.Core.Models;
using FuserSongManager.Infrastructure.Settings;

namespace FuserSongManager.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Settings_round_trip()
    {
        var store = new JsonSettingsStore(new AppDataPaths(_temp.File("app")));
        var settings = new AppSettings
        {
            FuserDirectory = @"C:\Games\Fuser",
            DisabledSongsDirectory = @"D:\disabled",
            LauncherType = LauncherType.Local,
            ExecutablePath = @"C:\Games\Fuser\Fuser.exe",
            ScanParallelism = 3,
        };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
        Assert.Contains("\"Local\"", File.ReadAllText(new AppDataPaths(_temp.File("app")).SettingsFile));
    }

    [Fact]
    public void Missing_or_corrupt_file_gives_default_settings()
    {
        var paths = new AppDataPaths(_temp.Path);
        var store = new JsonSettingsStore(paths);
        Assert.Equal(new AppSettings(), store.Load());

        File.WriteAllText(paths.SettingsFile, "{ not json");

        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void Library_paths_derive_from_the_fuser_folder()
    {
        var paths = LibraryPaths.FromFuserDirectory(@"C:\Games\Fuser");

        Assert.Equal(@"C:\Games\Fuser\Fuser\Content\Paks\custom_songs", paths.EnabledDirectory);
        Assert.Equal(@"C:\Games\Fuser\Fuser\Content\Paks\disabled_songs", paths.DisabledDirectory);
        Assert.Equal(@"D:\off", LibraryPaths.FromFuserDirectory(@"C:\Games\Fuser", @"D:\off").DisabledDirectory);
    }
}
