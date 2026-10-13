using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FuserSongManager.Core.Models;
using FuserSongManager.UI.ViewModels;
using FuserSongManager.UI.Views;
using static FuserSongManager.UI.Tests.TestLibrary;

namespace FuserSongManager.UI.Tests;

/// <summary>
/// Renders the real windows headlessly. Screenshots go to <c>FSM_SCREENSHOTS</c> when set, so the layout can be reviewed.
/// </summary>
public sealed class WindowTests : IDisposable
{
    private readonly TestLibrary _library = new();

    public void Dispose() => _library.Dispose();

    private static void Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        if (frame is not null && Environment.GetEnvironmentVariable("FSM_SCREENSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
#pragma warning disable CS0618 // the replacement needs an encoder-specific options type; PNG defaults are fine
            frame.Save(Path.Combine(folder, name + ".png"));
#pragma warning restore CS0618
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < timeout, "Timed out waiting for the window.");
            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task Main_window_shows_the_library_and_the_selected_song()
    {
        _library.AddPak(SongLocation.Enabled, "brightside_P", Meta("Mr. Brightside", "The Killers", Genre.Rock, 2003, 148));
        _library.AddPak(SongLocation.Enabled, "africa_P", Meta("Africa", "Toto", Genre.Pop, 1982, 93));
        _library.AddPak(SongLocation.Disabled, "around_P", Meta("Around the World", "Daft Punk", Genre.Dance, 1997, 121));
        _library.AddPak(SongLocation.Enabled, "broken_P");   // unreadable → flagged
        _library.AddPak(SongLocation.Disabled, "nosig_P", Meta("Missing Signature", "Someone", Genre.HipHop, 2015, 90), withSig: false);

        var viewModel = _library.CreateMainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();   // loading the window starts the scan
        await WaitUntil(() => viewModel.Rows.Count == 5 && !viewModel.IsBusy);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Library updated: 5 new", viewModel.StatusText);   // late progress reports must not overwrite it

        var grid = window.FindControl<DataGrid>("SongGrid")!;
        // Nothing is selected on its own: selecting reads the pak (album art), which must not slow down startup.
        Assert.Null(grid.SelectedItem);
        Assert.Null(viewModel.Detail);
        grid.SelectedItem = viewModel.Rows.Single(r => r.FileName == "brightside_P");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("brightside_P", viewModel.Detail?.FileName);
        Save(window, "main-window");

        // Ctrl+F jumps to the search box.
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);

        grid.SelectAll();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, viewModel.Selection.Count);
        Assert.Null(viewModel.Detail);
        Save(window, "main-window-multiselect");

        window.Close();
    }

    /// <summary>Real paks read with CUE4Parse, to check how real metadata looks in the list.</summary>
    [AvaloniaFact]
    public async Task Main_window_with_the_sample_paks()
    {
        var samples = Environment.GetEnvironmentVariable("FSM_SAMPLE_PAKS") ?? "";
        Assert.SkipUnless(Directory.Exists(samples), "Set FSM_SAMPLE_PAKS to a folder with sample paks to run this test.");

        foreach (var file in Directory.EnumerateFiles(samples, "*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)))
        {
            var target = Path.Combine(_library.Paths.EnabledDirectory, Path.GetRelativePath(samples, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        var scanner = new Core.Services.LibraryScanner();
        var services = new Services.AppServices(_library.Settings, _library.Repository, scanner,
            new Infrastructure.Pak.CueSongMetadataReader(Infrastructure.Pak.PatchedDlcCatalog.LoadEmbedded()),
            new Infrastructure.Files.SongFileService(), _library.Game, new Infrastructure.Files.SharpCompressImporter(scanner))
        {
            Media = new Infrastructure.Pak.PakMediaReader(),
            ArtCacheDirectory = Path.Combine(_library.Root, "art"),
            CreatePlayer = () => _library.Player,   // no sound card in tests
        };
        var viewModel = new MainViewModel(services, _library.Dialogs, _library.Shell);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await WaitUntil(() => viewModel.Rows.Count > 0 && !viewModel.IsBusy);
        Dispatcher.UIThread.RunJobs();

        Assert.All(viewModel.Rows, row => Assert.False(row.HasProblem, row.Problem));
        var grid = window.FindControl<DataGrid>("SongGrid")!;
        grid.Columns.Single(c => Equals(c.Header, "Artist")).Sort(System.ComponentModel.ListSortDirection.Ascending);
        grid.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        var detail = viewModel.Detail!;
        await detail.ArtLoaded;
        Assert.NotNull(detail.AlbumArt);
        await detail.Player!.PlayPauseCommand.ExecuteAsync(null);   // real pak read, decrypted; fake output
        Assert.True(_library.Player.Loaded!.Stems.Count >= 4);
        detail.Player.Stems[2].IsOn = false;
        detail.Player.PositionSeconds = 20;
        Dispatcher.UIThread.RunJobs();
        Save(window, "main-window-samples");
        window.Close();
    }

    [AvaloniaFact]
    public void Empty_library_explains_what_to_do()
    {
        var viewModel = _library.CreateMainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Save(window, "main-window-empty");
        window.Close();
    }

    [AvaloniaFact]
    public void First_run_and_settings_windows_render()
    {
        var v1 = new V1Library("songs.sqlite", new AppSettings { FuserDirectory = _library.FuserDirectory },
            [new Song { FileName = "a_P", Rating = 3 }, new Song { FileName = "b_P", Notes = "x" }]);
        var firstRun = new FirstRunWindow { DataContext = new FirstRunViewModel(v1, _library.Dialogs) };
        firstRun.Show();
        Save(firstRun, "first-run");
        firstRun.Close();

        var settings = new SettingsWindow
        {
            DataContext = new SettingsViewModel(new AppSettings { FuserDirectory = _library.Root, LauncherType = LauncherType.Local }, _library.Dialogs),
        };
        settings.Show();
        Save(settings, "settings-invalid");
        settings.Close();

        var message = MessageWindow.Create("Import finished", "Added 3 songs.\n\n1 item was skipped:\n\n• pack.zip › C_P: C_P.pak has no C_P.sig next to it.", "OK", null);
        message.Show();
        Save(message, "message");
        message.Close();
    }
}
