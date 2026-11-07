using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FuserSongManager.Core.Services;
using FuserSongManager.Core.Models;
using FuserSongManager.UI.Controls;
using FuserSongManager.UI.Services;
using FuserSongManager.UI.ViewModels;
using FuserSongManager.UI.Views;
using static FuserSongManager.UI.Tests.TestLibrary;
using GlyphPath = Avalonia.Controls.Shapes.Path;

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

        // D shows or hides the key and BPM over each title; the bottom bar shows the shortcut.
        var detail = () => Tile(window, "brightside_P").GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "A Minor · 148 BPM");
        Assert.True(detail().IsVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Key & BPM: ");
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.ShowTileDetails);
        Assert.False(detail().IsVisible);
        Save(window, "main-window-details-off");
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(detail().IsVisible);

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
        Assert.All(items, item => Assert.True(item.GetVisualDescendants().OfType<Controls.SweepFill>().Single().ShiftsColour));
        Assert.Same(Application.Current!.FindResource("Fsm.Selected"), items.Single(i => i.IsSelected).Background);
        Save(window, "main-window-sort-open");
        window.Close();
    }

    /// <summary>The tempo, key and mode panel next to the search, driven with the mouse and the keyboard.</summary>
    [AvaloniaFact]
    public async Task Filter_panel_opens_from_the_search_and_filters_by_tempo_key_and_mode()
    {
        _library.AddPak(SongLocation.Enabled, "brightside_P", Meta("Mr. Brightside", "The Killers", Genre.Rock, 2003, 148));
        _library.AddPak(SongLocation.Enabled, "africa_P", Meta("Africa", "Toto", Genre.Pop, 1982, 93) with { Key = MusicalKey.B, Mode = SongMode.Major });
        _library.AddPak(SongLocation.Enabled, "around_P", Meta("Around the World", "Daft Punk", Genre.Dance, 1997, 121));
        var viewModel = _library.CreateMainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await WaitUntil(() => viewModel.Rows.Count == 3 && !viewModel.IsBusy);
        viewModel.Click(Row(viewModel, "brightside_P"));
        Dispatcher.UIThread.RunJobs();

        var button = window.FindControl<Button>("FilterButton")!;

        // Hovering the button shows the summary at once; leaving it hides it at once.
        var center = button.TranslatePoint(new Point(22, 22), window)!.Value;
        window.MouseMove(center);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ToolTip.GetIsOpen(button));
        Save(window, "filters-summary");
        window.MouseMove(new Point(5, 400));
        Dispatcher.UIThread.RunJobs();
        Assert.False(ToolTip.GetIsOpen(button));

        button.Flyout!.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        var sliders = button.Flyout is Flyout { Content: Control content }
            ? content.GetVisualDescendants().OfType<Controls.GameSlider>().ToList() : [];
        Assert.Equal(2, sliders.Count);
        var (tempo, key) = (sliders[0], sliders[1]);
        Assert.Equal((93d, 148d), (tempo.Minimum, tempo.Maximum));
        var popup = TopLevel.GetTopLevel(tempo)!;
        SavePopup(popup, "filters-open");

        // Hovering the X must not turn it white on its white disc.
        var close = ((Flyout)button.Flyout).Content is Control card ? card.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("close")) : null;
        Assert.NotNull(close);
        popup.MouseMove(close.TranslatePoint(new Point(24, 24), popup)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(close.IsPointerOver);
        var icon = close.GetVisualDescendants().OfType<PathIcon>().Single();
        Assert.Equal(Color.Parse("#1A1238"), ((ISolidColorBrush)icon.Foreground!).Color);
        SavePopup(popup, "filters-close-hover");
        popup.MouseMove(new Point(5, 5));

        // Drag the lower tempo thumb to the middle: the slower song goes once the slider rests.
        var y = tempo.Bounds.Height / 2;
        popup.MouseDown(tempo.TranslatePoint(new Point(14, y), popup)!.Value, MouseButton.Left);
        popup.MouseMove(tempo.TranslatePoint(new Point(tempo.Bounds.Width / 2, y), popup)!.Value);
        popup.MouseUp(tempo.TranslatePoint(new Point(tempo.Bounds.Width / 2, y), popup)!.Value, MouseButton.Left);
        Assert.InRange(viewModel.BpmLow, 115, 126);
        await Task.Delay(MainViewModel.SearchDelay * 3);
        Assert.DoesNotContain(viewModel.VisibleSongs, r => r.FileName == "africa_P");
        Assert.Contains("on", button.Classes);

        // The arrows move the focused slider, not the selection in the grid behind the panel.
        popup.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("brightside_P", viewModel.Detail?.FileName);

        // Hovering shows the value under the pointer, as in the game.
        popup.MouseMove(tempo.TranslatePoint(new Point(tempo.Bounds.Width * 0.8, y), popup)!.Value);
        await Task.Delay(300);   // the row's colour
        SavePopup(popup, "filters-tempo-hover");

        // The slider takes clicks a little above and below its track, not only on the thin line.
        var nearMiss = key.TranslatePoint(new Point(key.Bounds.Width - 14, key.Bounds.Height / 2 - 16), popup)!.Value;
        popup.MouseDown(nearMiss, MouseButton.Left);
        popup.MouseUp(nearMiss, MouseButton.Left);
        Assert.Equal("B", viewModel.KeyText);
        viewModel.KeyIndex = 0;

        key.Focus();
        popup.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Assert.Equal("B", viewModel.KeyText);
        Assert.Empty(viewModel.VisibleSongs);   // the only song in B is too slow
        viewModel.ResetAdvancedFiltersCommand.Execute(null);
        key.Focus();
        popup.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        viewModel.ModeChoices.Single(c => c.Label == "Major").SelectCommand.Execute(null);
        Assert.Equal(["Africa"], viewModel.VisibleSongs.Select(r => r.Title));
        Dispatcher.UIThread.RunJobs();
        SavePopup(popup, "filters-key-mode");

        button.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        Save(window, "filters-applied");
        window.Close();
    }

    /// <summary>Settings and messages open over the main window, in its dialog layer, and block what is under them.</summary>
    [AvaloniaFact]
    public async Task Dialogs_open_inside_the_main_window_and_block_the_library()
    {
        _library.AddPak(SongLocation.Enabled, "brightside_P", Meta("Mr. Brightside", "The Killers", Genre.Rock, 2003, 148));
        _library.AddPak(SongLocation.Enabled, "africa_P", Meta("Africa", "Toto", Genre.Pop, 1982, 93));
        var viewModel = _library.CreateMainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await WaitUntil(() => viewModel.Rows.Count == 2 && !viewModel.IsBusy);
        viewModel.Click(Row(viewModel, "brightside_P"));
        Dispatcher.UIThread.RunJobs();
        var dialogs = new AvaloniaDialogService(() => window);
        var host = window.FindControl<DialogHost>("Dialogs")!;
        Assert.False(host.IsVisible);

        // Settings: a panel in the window, with focus inside it.
        var settings = new SettingsViewModel(new AppSettings { FuserDirectory = _library.FuserDirectory, LauncherType = LauncherType.Steam }, _library.Dialogs);
        var pending = dialogs.ShowSettingsAsync(settings);
        Dispatcher.UIThread.RunJobs();
        Assert.True(host.IsOpen);
        Assert.True(host.IsVisible);
        var panel = window.GetVisualDescendants().OfType<SettingsPanel>().Single();
        Assert.True(panel.GetVisualDescendants().OfType<ModalPanel>().Single().ShowClose);
        Assert.True(IsInside(window.FocusManager!.GetFocusedElement(), host));
        Save(window, "settings-panel");

        // The shortcuts of the library do nothing while it is open: Ctrl+A would select both songs, Esc closes the dialog
        // instead of clearing the selection.
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(viewModel.Selection);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(await pending);
        Assert.False(host.IsVisible);
        Assert.Single(viewModel.Selection);

        // Saving closes with true.
        pending = dialogs.ShowSettingsAsync(settings);
        Dispatcher.UIThread.RunJobs();
        Click(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveButton"));
        Assert.True(await pending);
        Assert.False(host.IsOpen);

        // The X is the same as Cancel.
        pending = dialogs.ShowSettingsAsync(settings);
        Dispatcher.UIThread.RunJobs();
        Click(window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("close")));
        Assert.False(await pending);

        // A confirmation over a message: the second is on top and the first comes back when it closes.
        var message = dialogs.ShowMessageAsync("Import finished", "Added 3 songs.");
        var confirm = dialogs.ConfirmAsync("Enable songs", "Enable 2 songs?", "Enable");
        Dispatcher.UIThread.RunJobs();
        var shown = window.GetVisualDescendants().OfType<MessagePanel>().Single();
        Assert.Equal("Enable songs", shown.Card.Title);
        Assert.False(shown.Card.ShowClose);
        Save(window, "message-panel");
        Click(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmButton"));
        Assert.True(await confirm);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Import finished", window.GetVisualDescendants().OfType<MessagePanel>().Single().Card.Title);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);   // the default button
        Dispatcher.UIThread.RunJobs();
        await message;
        Assert.False(host.IsOpen);
        Assert.Single(viewModel.Selection);   // Enter did not enable or disable the selected song

        // Closing the window releases whoever waits for a dialog.
        pending = dialogs.ShowSettingsAsync(settings);
        window.Close();
        Assert.False(await pending);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static bool IsInside(object? element, Visual container) =>
        element is Visual visual && visual.GetSelfAndVisualAncestors().Contains(container);

    [Fact]
    public void Hover_colour_drifts_through_the_game_colours_and_back()
    {
        static string Left(double seconds) => Controls.SweepFill.HoverBrushAt(seconds).GradientStops[0].Color.ToString();

        Assert.Equal("#ff6c55ee", Left(0));      // violet
        Assert.Equal("#ffc63cc8", Left(2.5));    // magenta
        Assert.Equal("#ffff5a55", Left(5));      // coral
        Assert.Equal("#ffff7a52", Left(7.5));    // orange
        Assert.Equal("#ffff5a55", Left(10));     // and back
        Assert.Equal("#ff6c55ee", Left(15));
        Assert.NotEqual(Left(1), Left(1.5));     // in between, it moves
    }

    private static void SavePopup(TopLevel popup, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = popup.CaptureRenderedFrame();
        if (frame is not null && Environment.GetEnvironmentVariable("FSM_SCREENSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
#pragma warning disable CS0618
            frame.Save(Path.Combine(folder, name + ".png"));
#pragma warning restore CS0618
        }
    }

    /// <summary>The riser button next to play: the game's round lilac disc, its icon still visible when hovered, and a pulse while it plays.</summary>
    [AvaloniaFact]
    public async Task Riser_button_sits_next_to_play_and_shows_its_icon_idle_hovered_and_playing()
    {
        _library.AddPak(SongLocation.Enabled, "brightside_P", Meta("Mr. Brightside", "The Killers", Genre.Rock, 2003, 148));
        var viewModel = _library.CreateMainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await WaitUntil(() => viewModel.Rows.Count == 1 && !viewModel.IsBusy);
        viewModel.Click(Row(viewModel, "brightside_P"));
        Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("riser"));
        var play = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("play"));
        Assert.True(button.IsVisible);
        Assert.True(button.TranslatePoint(new Point(), window)!.Value.X > play.TranslatePoint(new Point(), window)!.Value.X);
        Assert.Equal(play.TranslatePoint(new Point(0, 28), window)!.Value.Y, button.TranslatePoint(new Point(0, 24), window)!.Value.Y, 1);   // same row, centred
        Save(window, "riser-idle");

        // The icon is centred in the disc: exactly in height, and in width shifted a little to the right, because the
        // three nodes on its left weigh more than the arrow.
        var glyph = button.GetVisualDescendants().OfType<GlyphPath>().Single();
        var glyphCentre = glyph.TranslatePoint(new Point(glyph.Bounds.Width / 2, glyph.Bounds.Height / 2), window)!.Value;
        var discCentre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        Assert.Equal(discCentre.Y, glyphCentre.Y, 0.5);
        Assert.InRange(glyphCentre.X - discCentre.X, 0.5, 2.5);

        window.MouseMove(button.TranslatePoint(new Point(24, 24), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsPointerOver);
        var icon = button.GetVisualDescendants().OfType<GlyphPath>().Single();
        Assert.Equal(Color.Parse("#1A1238"), ((ISolidColorBrush)icon.Fill!).Color);   // dark on the light disc
        Save(window, "riser-hover");

        await viewModel.Detail!.Player!.Riser!.PlayCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("playing", button.Classes);
        Save(window, "riser-playing");

        // The effect selector, with the names DJs use.
        var effects = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.ItemsSource == viewModel.Detail.Player.Riser!.Effects);
        Assert.Equal("Low Pass Filter", ((Option<RiserEffect>)effects.SelectedItem!).Label);
        effects.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Save(window, "riser-effects-open");
        window.Close();
    }

    [AvaloniaFact]
    public void Search_box_renders_with_text_and_focus()
    {
        var viewModel = _library.CreateMainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var search = window.FindControl<TextBox>("SearchBox")!;
        Save(window, "search-empty");

        search.Focus();
        window.KeyTextInput("Killers");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Killers", search.Text);
        Save(window, "search-typed");
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
    public void First_run_and_start_failure_windows_render()
    {
        var v1 = new V1Library("songs.sqlite", new AppSettings { FuserDirectory = _library.FuserDirectory },
            [new Song { FileName = "a_P", Rating = 3 }, new Song { FileName = "b_P", Notes = "x" }]);
        var firstRun = new FirstRunWindow { DataContext = new FirstRunViewModel(v1, _library.Dialogs) };
        firstRun.Show();
        Save(firstRun, "first-run");
        firstRun.Close();

        var message = MessageWindow.Create("Import finished", "Added 3 songs.\n\n1 item was skipped:\n\n• pack.zip › C_P: C_P.pak has no C_P.sig next to it.", "OK", null);
        message.Show();
        Save(message, "message");
        message.Close();
    }
}
