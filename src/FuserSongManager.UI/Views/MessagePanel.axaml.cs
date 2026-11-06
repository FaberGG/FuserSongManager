using Avalonia.Controls;
using Avalonia.Media;
using FuserSongManager.UI.Controls;

namespace FuserSongManager.UI.Views;

/// <summary>Message or confirmation, as a dialog of the main window's <see cref="DialogHost"/>. Closes with true when the main button is pressed.</summary>
public partial class MessagePanel : UserControl
{
    public MessagePanel()
    {
        InitializeComponent();
        ConfirmButton.Click += (_, _) => DialogHost.CloseFrom(this, true);
        CancelButton.Click += (_, _) => DialogHost.CloseFrom(this, false);
    }

    public static MessagePanel Create(string title, string message, string confirmText, string? cancelText)
    {
        var panel = new MessagePanel();
        panel.Card.Title = title;
        panel.MessageText.Text = message;
        // Short messages are centred like the game's; lists read better aligned left.
        panel.MessageText.TextAlignment = message.Contains('\n') ? TextAlignment.Left : TextAlignment.Center;
        panel.ConfirmButton.Content = confirmText;
        panel.CancelButton.Content = cancelText;
        panel.CancelButton.IsVisible = cancelText is not null;
        return panel;
    }
}
