using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FuserSongManager.UI.Controls;

/// <summary>
/// The layer over a window where dialogs are shown without opening operating system windows: a dim veil over
/// everything under it, and the dialog (usually a <see cref="ModalPanel"/>) centred on top. It is the last child
/// of the window's root panel, so it covers the rest and takes its clicks.
/// <para>
/// <see cref="ShowAsync"/> returns when the dialog closes, with the result the dialog closed with; Esc and the
/// panel's close button close with false. Dialogs stack: one opened from another is shown over it and the first
/// comes back when it closes. Focus goes into the dialog, cycles inside it with Tab, and returns to where it
/// was when the last dialog closes.
/// </para>
/// </summary>
public sealed class DialogHost : ContentControl
{
    private sealed record Frame(Control Content, TaskCompletionSource<bool> Result, IInputElement? Restore);

    private readonly Stack<Frame> _frames = new();

    public DialogHost()
    {
        IsVisible = false;
        // The close button of a panel means "cancel".
        AddHandler(ModalPanel.CloseRequestedEvent, (_, e) =>
        {
            Close(false);
            e.Handled = true;
        });
    }

    public bool IsOpen => _frames.Count > 0;

    /// <summary>Shows <paramref name="content"/> and waits for it to close. The result is what <see cref="Close"/> got.</summary>
    public Task<bool> ShowAsync(Control content)
    {
        var restore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var frame = new Frame(content, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), restore);
        _frames.Push(frame);
        Present(frame);
        return frame.Result.Task;
    }

    /// <summary>Closes the dialog on top with <paramref name="result"/>.</summary>
    public void Close(bool result)
    {
        if (!_frames.TryPop(out var frame))
            return;

        if (_frames.TryPeek(out var below))
        {
            Present(below);
        }
        else
        {
            Content = null;
            IsVisible = false;
        }
        frame.Result.TrySetResult(result);

        if (frame.Restore is InputElement { Focusable: true } element)
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsOpen && element.IsEffectivelyVisible && element.IsEffectivelyEnabled)
                    element.Focus(NavigationMethod.Tab);
            }, DispatcherPriority.Loaded);
    }

    /// <summary>Closes every dialog with false, e.g. when the window closes.</summary>
    public void CloseAll()
    {
        while (IsOpen)
            Close(false);
    }

    /// <summary>The host of the window <paramref name="control"/> is in, if it is inside a dialog.</summary>
    public static DialogHost? Of(Visual control) => control.FindAncestorOfType<DialogHost>();

    /// <summary>For the buttons of a dialog: closes the dialog they are in.</summary>
    public static void CloseFrom(Visual control, bool result) => Of(control)?.Close(result);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Whatever is inside gets Esc first (a drop-down closes itself); what it leaves closes the dialog.
        if (e.Key == Key.Escape && !e.Handled && e.KeyModifiers == KeyModifiers.None)
        {
            Close(false);
            e.Handled = true;
        }
    }

    private void Present(Frame frame)
    {
        Content = frame.Content;
        IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_frames.TryPeek(out var top) && top == frame)
                FocusFirst(frame.Content);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// A text box if there is one (the dialog is a form), else the default button, else the first control that
    /// takes focus. The close button is left out: it is a way out, not where to start.
    /// </summary>
    private static void FocusFirst(Control content)
    {
        var focusable = content.GetVisualDescendants().OfType<InputElement>()
            .Where(e => e.Focusable && e.IsEffectivelyEnabled && e.IsEffectivelyVisible
                        && !(e is Button button && button.Classes.Contains("close")))
            .ToList();
        var target = focusable.OfType<TextBox>().FirstOrDefault()
                     ?? (InputElement?)focusable.OfType<Button>().FirstOrDefault(b => b.IsDefault)
                     ?? focusable.FirstOrDefault();
        target?.Focus(NavigationMethod.Tab);
    }
}
