using Avalonia.Controls;

namespace FuserSongManager.UI.Views;

/// <summary>Closes with true when saved.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        SaveButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
