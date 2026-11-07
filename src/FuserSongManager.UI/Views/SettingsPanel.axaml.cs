using Avalonia.Controls;
using FuserSongManager.UI.Controls;

namespace FuserSongManager.UI.Views;

/// <summary>The settings, as a dialog of the main window's <see cref="DialogHost"/>. Closes with true when saved.</summary>
public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
        SaveButton.Click += (_, _) => DialogHost.CloseFrom(this, true);
        CancelButton.Click += (_, _) => DialogHost.CloseFrom(this, false);
    }
}
