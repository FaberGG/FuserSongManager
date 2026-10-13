using FuserSongManager.Core.Models;
using FuserSongManager.UI.ViewModels;

namespace FuserSongManager.UI.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly TestLibrary _library = new();

    public void Dispose() => _library.Dispose();

    [Fact]
    public void A_folder_that_is_not_a_fuser_install_cannot_be_saved()
    {
        var settings = new SettingsViewModel(new AppSettings(), _library.Dialogs);
        Assert.False(settings.CanSave);
        Assert.Equal("Choose the folder where Fuser is installed.", settings.FolderError);

        settings.FuserDirectory = _library.Root;
        Assert.Contains("does not look like a Fuser install", settings.FolderError);

        settings.FuserDirectory = _library.Paths.PaksDirectory;
        Assert.True(settings.CanSave);
        Assert.Equal(_library.FuserDirectory, settings.ToSettings(new AppSettings()).FuserDirectory);
    }

    [Fact]
    public void Local_launcher_needs_an_existing_executable()
    {
        var settings = new SettingsViewModel(new AppSettings { FuserDirectory = _library.FuserDirectory }, _library.Dialogs)
        {
            LauncherType = LauncherType.Local,
        };
        Assert.True(settings.IsExecutableNeeded);
        Assert.False(settings.CanSave);

        var exe = Path.Combine(_library.Root, "start.bat");
        File.WriteAllText(exe, "@echo off");
        settings.ExecutablePath = exe;
        Assert.True(settings.CanSave);

        settings.LauncherType = LauncherType.Epic;
        Assert.Contains("not supported", settings.LauncherNote);
    }

    [Fact]
    public void First_run_takes_the_folder_from_v1_and_offers_its_library()
    {
        var v1Songs = new[]
        {
            new Song { FileName = "a_P", Rating = 4, Notes = "nice" },
            new Song { FileName = "b_P" },
        };
        var v1 = new V1Library("songs.sqlite", new AppSettings { FuserDirectory = _library.FuserDirectory, LauncherType = LauncherType.Local }, v1Songs);

        var firstRun = new FirstRunViewModel(v1, _library.Dialogs);

        Assert.Equal(_library.FuserDirectory, firstRun.FuserDirectory);
        Assert.Equal(LauncherType.Local, firstRun.LauncherType);
        Assert.True(firstRun.HasV1);
        Assert.True(firstRun.ImportV1);
        Assert.Contains("2 songs (1 rated, 1 with notes)", firstRun.V1Summary);
        Assert.Contains("taken from your v1 settings", firstRun.Intro);
    }

    [Fact]
    public void First_run_without_v1_asks_for_the_folder()
    {
        var firstRun = new FirstRunViewModel(null, _library.Dialogs);

        Assert.False(firstRun.HasV1);
        Assert.False(firstRun.ImportV1);
        Assert.StartsWith("Choose the folder", firstRun.Intro);
    }
}
