using Avalonia.Controls;
using FuserSongManager.UI.ViewModels;

namespace FuserSongManager.UI.Views;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
        ContinueButton.Click += (_, _) =>
        {
            if (DataContext is FirstRunViewModel { CanSave: true } viewModel)
            {
                ContinueButton.IsEnabled = false;
                Completed?.Invoke(this, viewModel);
            }
        };
    }

    public void AllowContinue() => ContinueButton.IsEnabled = true;

    /// <summary>The user confirmed the settings; the app saves them and opens the main window.</summary>
    public event EventHandler<FirstRunViewModel>? Completed;
}
