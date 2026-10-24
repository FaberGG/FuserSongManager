using Avalonia.Controls;

namespace FuserSongManager.UI.Views;

/// <summary>Message or confirmation dialog. Closes with true when the main button is pressed.</summary>
public partial class MessageWindow : Window
{
    public MessageWindow()
    {
        InitializeComponent();
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    public static MessageWindow Create(string title, string message, string confirmText, string? cancelText)
    {
        var window = new MessageWindow { Title = title };
        window.TitleText.Text = title;
        window.MessageText.Text = message;
        // Short messages are centred like the game's; lists read better aligned left.
        window.MessageText.TextAlignment = message.Contains('\n') ? Avalonia.Media.TextAlignment.Left : Avalonia.Media.TextAlignment.Center;
        window.ConfirmButton.Content = confirmText;
        window.CancelButton.Content = cancelText;
        window.CancelButton.IsVisible = cancelText is not null;
        return window;
    }
}
