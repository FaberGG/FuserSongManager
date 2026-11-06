using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace FuserSongManager.UI.Controls;

/// <summary>
/// The card of the app's in-window panels (the tempo / key / mode filters, Settings, messages): a dark rounded
/// box with an optional <see cref="Title"/>, the content, an optional <see cref="Footer"/> for the buttons and,
/// when <see cref="ShowClose"/> is on, the round close button over its top-right corner. The control only draws
/// the card: whoever shows it decides what closing means, by handling <see cref="CloseRequested"/>
/// (a <see cref="DialogHost"/> does it for dialogs; a flyout hides itself).
/// </summary>
/// <remarks>The corner button sticks out of the card, so the control is wider and taller than the card by its overhang.</remarks>
public sealed class ModalPanel : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ModalPanel, string?>(nameof(Title));

    public static readonly StyledProperty<TextAlignment> TitleAlignmentProperty =
        AvaloniaProperty.Register<ModalPanel, TextAlignment>(nameof(TitleAlignment));

    /// <summary>Usually the buttons, under the content.</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<ModalPanel, object?>(nameof(Footer));

    public static readonly StyledProperty<bool> ShowCloseProperty =
        AvaloniaProperty.Register<ModalPanel, bool>(nameof(ShowClose), true);

    /// <summary>Raised when the close button is pressed.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> CloseRequestedEvent =
        RoutedEvent.Register<ModalPanel, RoutedEventArgs>(nameof(CloseRequested), RoutingStrategies.Bubble);

    private Button? _closeButton;

    public ModalPanel()
    {
        UpdateClosablePseudoClass();
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public TextAlignment TitleAlignment
    {
        get => GetValue(TitleAlignmentProperty);
        set => SetValue(TitleAlignmentProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool ShowClose
    {
        get => GetValue(ShowCloseProperty);
        set => SetValue(ShowCloseProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? CloseRequested
    {
        add => AddHandler(CloseRequestedEvent, value);
        remove => RemoveHandler(CloseRequestedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_closeButton is not null)
            _closeButton.Click -= OnCloseClick;
        _closeButton = e.NameScope.Find<Button>("PART_Close");
        if (_closeButton is not null)
            _closeButton.Click += OnCloseClick;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowCloseProperty)
            UpdateClosablePseudoClass();
    }

    // The card leaves room for the close button only when there is one.
    private void UpdateClosablePseudoClass() => PseudoClasses.Set(":closable", ShowClose);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
}
