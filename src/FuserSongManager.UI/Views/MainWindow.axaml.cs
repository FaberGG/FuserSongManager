using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FuserSongManager.UI.ViewModels;

namespace FuserSongManager.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // DataGrid.SelectedItems cannot be bound, so the selection is pushed to the view model.
        SongGrid.SelectionChanged += (_, _) => ViewModel?.SetSelection(SongGrid.SelectedItems.OfType<SongRowViewModel>());
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
        ViewModel?.Dispose();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
