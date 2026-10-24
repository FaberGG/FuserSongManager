using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FuserSongManager.Core.Services;
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

        // Nothing is selected on its own: selecting reads the pak (album art), which must not slow down startup.
        Assert.Empty(viewModel.Selection);
        Assert.Null(viewModel.Detail);
        Assert.Equal(5, Tiles(window).Count);

        Tile(window, "brightside_P").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("brightside_P", viewModel.Detail?.FileName);
        Assert.True(Row(viewModel, "brightside_P").IsSelected);
        Save(window, "main-window");

        // Ctrl+F jumps to the search box; Esc leaves it, and the arrows move between songs.
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(viewModel.VisibleSongs[IndexOf(viewModel, "brightside_P") - 1].FileName, viewModel.Detail?.FileName);

        // Z turns autoplay on and off; it is remembered in the settings.
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Assert.True(viewModel.IsAutoplayOn);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        Assert.False(viewModel.IsAutoplayOn);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, viewModel.Selection.Count);
        Assert.Null(viewModel.Detail);
        Save(window, "main-window-multiselect");

        window.Close();
    }

    private static List<Button> Tiles(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tile"))];

    private static Button Tile(Window window, string fileName) =>
        Tiles(window).Single(b => (b.DataContext as SongRowViewModel)?.FileName == fileName);

    private static SongRowViewModel Row(MainViewModel viewModel, string fileName) => viewModel.Rows.Single(r => r.FileName == fileName);

    private static int IndexOf(MainViewModel viewModel, string fileName) =>
        viewModel.VisibleSongs.Select((r, i) => (r, i)).Single(x => x.r.FileName == fileName).i;

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
        // Every part's instrument is read with the metadata (for the tiles' icons and the discs).
        Assert.All(viewModel.Rows, row => Assert.Equal(4, row.Parts.Count));
        viewModel.SelectedSort = viewModel.SortOptions.Single(o => o.Value == SongSort.Artist);
        viewModel.Click(viewModel.VisibleSongs[0]);
        Dispatcher.UIThread.RunJobs();
        await viewModel.Thumbnails.Idle;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(viewModel.Rows, row => row.Thumbnail is not null);
        var detail = viewModel.Detail!;
        await detail.ArtLoaded;
        Assert.NotNull(detail.AlbumArt);
        await detail.Player!.PlayPauseCommand.ExecuteAsync(null);   // real pak read, decrypted; fake output
        Assert.True(_library.Player.Loaded!.Stems.Count >= 4);
        Assert.True(_library.Player.Loop);   // the preview loops like the game
        detail.Player.Stems[2].IsOn = false;
        detail.Player.PositionSeconds = 20;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Phrase", detail.Player.BarText);
        Save(window, "main-window-samples");
        window.Close();
    }

    /// <summary>The side panel scrolled to its end, the tile hover and the open sort list, for review.</summary>
    [AvaloniaFact]
    public async Task Song_panel_hover_and_drop_down_render()
    {
        _library.AddPak(SongLocation.Enabled, "brightside_P", Meta("Mr. Brightside", "The Killers", Genre.Rock, 2003, 148));
        _library.AddPak(SongLocation.Enabled, "africa_P", Meta("Africa", "Toto", Genre.Pop, 1982, 93));
        var viewModel = _library.CreateMainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await WaitUntil(() => viewModel.Rows.Count == 2 && !viewModel.IsBusy);
        Dispatcher.UIThread.RunJobs();

        viewModel.Click(Row(viewModel, "brightside_P"));
        await viewModel.Detail!.Player!.PlayPauseCommand.ExecuteAsync(null);
        viewModel.Detail.Player.Stems[1].IsOn = false;
        viewModel.Detail.Player.PositionSeconds = 25;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Drums", "Bass guitar", "Synth", "Vocals"], viewModel.Detail.Player.Stems.Select(s => s.InstrumentName));
        Assert.True(viewModel.Detail.Player.Stems[0].IsSpinning);
        Assert.False(viewModel.Detail.Player.Stems[1].IsSpinning);   // muted

        window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "DetailScroller").ScrollToEnd();
        // Hover the first tile: it grows and shows genre, year and the four instruments.
        var tile = Tile(window, "africa_P");
        window.MouseMove(tile.TranslatePoint(new Point(40, 40), window)!.Value);
        await Task.Delay(300);   // the hover transitions
        Dispatcher.UIThread.RunJobs();
        Save(window, "main-window-panel-hover");

        var sort = window.GetVisualDescendants().OfType<ComboBox>().First();
        sort.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var items = sort.GetLogicalDescendants().OfType<ComboBoxItem>().ToList();
        Assert.NotEmpty(items);
        // The options use the app's template (with the sweep), and the chosen one is magenta like the game's.
        Assert.All(items, item => Assert.Single(item.GetVisualDescendants().OfType<Controls.SweepFill>()));
        Assert.Same(Application.Current!.FindResource("Fsm.Selected"), items.Single(i => i.IsSelected).Background);
        Save(window, "main-window-sort-open");
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
