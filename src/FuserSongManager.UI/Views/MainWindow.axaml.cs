using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FuserSongManager.UI.ViewModels;

namespace FuserSongManager.UI.Views;

public partial class MainWindow : Window
{
    /// <summary>Tiles are at least this wide; the grid fits as many columns as it can.</summary>
    public const double MinTileWidth = 180;
    private const double TileSpacing = 8;

    // Modifiers of the last click, read when the tile's Click arrives (Click does not carry them).
    private KeyModifiers _clickModifiers;

    public MainWindow()
    {
        InitializeComponent();
        TileList.SizeChanged += (_, e) => UpdateColumns(e.NewSize.Width);
        // Tunnel: shortcuts win over the focused control (a tile must not also "click" on Space or Enter).
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnShortcutKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, e) => _clickModifiers = e.KeyModifiers, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (ViewModel is { } viewModel)
            await viewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dialogs.CloseAll();   // nobody is left waiting for a dialog of a closed window
        ViewModel?.Dispose();
        base.OnClosed(e);
    }

    private void UpdateColumns(double width)
    {
        if (ViewModel is { } viewModel && width > 0)
            viewModel.Columns = Math.Max(1, (int)((width + TileSpacing) / (MinTileWidth + TileSpacing)));
    }

    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SongRowViewModel row } && ViewModel is { } viewModel)
        {
            var modifiers = _clickModifiers;
            _clickModifiers = KeyModifiers.None;
            viewModel.Click(row, modifiers.HasFlag(KeyModifiers.Control), modifiers.HasFlag(KeyModifiers.Shift));
        }
    }

    private void OnTileLoaded(object? sender, RoutedEventArgs e) => RequestThumbnail(sender);

    private void OnTileDataContextChanged(object? sender, EventArgs e) => RequestThumbnail(sender);

    /// <summary>Tiles are created as they scroll into view; that is when their art is loaded.</summary>
    private static void RequestThumbnail(object? sender)
    {
        if (sender is Control { DataContext: SongRowViewModel row, IsLoaded: true })
            row.RequestThumbnail();
    }

    private bool IsTyping => FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>
    /// Keys pressed in a drop-down, a menu or the filter panel route through the window too; they belong to that
    /// popup (the arrows move its sliders and items, Space and Enter press its buttons, Esc closes it).
    /// </summary>
    private static bool IsInPopup(KeyEventArgs e) =>
        e.Source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v is PopupRoot or OverlayPopupHost);

    private void OnCloseFilters(object? sender, RoutedEventArgs e) => FilterButton.Flyout?.Hide();

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        // While a dialog is open the window's shortcuts must not act on the library under it. Keys meant for the
        // dialog (arrows, Space, Enter, Tab) go on to it; Esc closes it even if focus is not inside it.
        if (Dialogs.IsOpen)
        {
            if (e.Key == Key.Escape && !IsInPopup(e) && !(e.Source is Visual source && source.GetSelfAndVisualAncestors().Contains(Dialogs)))
            {
                Dialogs.Close(false);
                e.Handled = true;
            }
            return;
        }
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // These work everywhere, even while typing.
        switch (e.Key)
        {
            case Key.F when control:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            case Key.I when control:
                Run(viewModel.ImportCommand);
                e.Handled = true;
                return;
            case Key.F5:
                Run(viewModel.RescanCommand);
                e.Handled = true;
                return;
            case Key.Escape when IsTyping:
                // Back to the grid, so the arrows and the other shortcuts work again.
                GridScroller.Focus();
                e.Handled = true;
                return;
        }
        if (IsTyping || IsInPopup(e) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        switch (e.Key)
        {
            case Key.Z when !control:
                viewModel.ToggleAutoplayCommand.Execute(null);
                break;
            case Key.D when !control:
                viewModel.ToggleTileDetailsCommand.Execute(null);
                break;
            case Key.Space:
                Run(viewModel.PlayPauseCommand);
                break;
            case Key.Enter:
                Run(viewModel.ToggleSelectedEnabledCommand);
                break;
            case Key.Left:
                Move(viewModel, -1);
                break;
            case Key.Right:
                Move(viewModel, 1);
                break;
            case Key.Up:
                Move(viewModel, -viewModel.Columns);
                break;
            case Key.Down:
                Move(viewModel, viewModel.Columns);
                break;
            case Key.A when control:
                viewModel.SelectAllVisible();
                break;
            case Key.Escape:
                viewModel.SetSelection([]);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Buttons click on Space when the key goes up: keep the shortcut from also pressing the focused button.
    private void OnShortcutKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter && !IsTyping && !IsInPopup(e) && !Dialogs.IsOpen)
            e.Handled = true;
    }

    private void Move(MainViewModel viewModel, int delta)
    {
        var index = viewModel.MoveSelection(delta);
        if (index >= 0)
            TileList.ScrollIntoView(index / Math.Max(1, viewModel.Columns));
    }

    private static void Run(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }
}
