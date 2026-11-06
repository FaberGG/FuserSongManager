using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace FuserSongManager.UI.Controls;

/// <summary>
/// The slider of the game's tempo and key settings: a light track with round lilac thumbs and, under the pointer,
/// a small label with the value it would pick. With <see cref="IsRange"/> it has two thumbs and picks the range
/// between <see cref="LowerValue"/> and <see cref="UpperValue"/>; without it, one thumb on <see cref="UpperValue"/>.
/// Values are whole numbers. The arrow keys move the last thumb used (Page Up/Down by ten, Home/End to the ends).
/// </summary>
public sealed class GameSlider : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<GameSlider, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<GameSlider, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> LowerValueProperty =
        AvaloniaProperty.Register<GameSlider, double>(nameof(LowerValue), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> UpperValueProperty =
        AvaloniaProperty.Register<GameSlider, double>(nameof(UpperValue), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsRangeProperty =
        AvaloniaProperty.Register<GameSlider, bool>(nameof(IsRange));

    /// <summary>Format of the hover label, e.g. "{0} BPM".</summary>
    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<GameSlider, string>(nameof(ValueFormat), "{0}");

    /// <summary>Hover label per value, from <see cref="Minimum"/> up; overrides <see cref="ValueFormat"/>.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> ValueLabelsProperty =
        AvaloniaProperty.Register<GameSlider, IReadOnlyList<string>?>(nameof(ValueLabels));

    private const double ThumbRadius = 11;
    private const double Inset = ThumbRadius + 3;
    private const double TrackHeight = 6;
    private const double LabelGap = 8;

    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#59FFFFFF"));
    private static readonly IBrush TrackOn = new SolidColorBrush(Color.Parse("#F2F0EEF8"));
    private static readonly IBrush Thumb = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#F3B2EA"), 0), new GradientStop(Color.Parse("#8F8CF5"), 1) },
    };
    private static readonly IPen ThumbEdge = new Pen(new SolidColorBrush(Color.Parse("#66FFFFFF")), 1.5);
    private static readonly IPen FocusRing = new Pen(Brushes.White, 2);
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.Parse("#F2F4F0FA"));
    private static readonly IBrush LabelInk = new SolidColorBrush(Color.Parse("#1A1238"));

    private bool _upperActive = true;
    private double? _hoverX;

    static GameSlider()
    {
        AffectsRender<GameSlider>(MinimumProperty, MaximumProperty, LowerValueProperty, UpperValueProperty, IsRangeProperty,
            ValueFormatProperty, ValueLabelsProperty, IsFocusedProperty);
        FocusableProperty.OverrideDefaultValue<GameSlider>(true);
        CursorProperty.OverrideDefaultValue<GameSlider>(new Cursor(StandardCursorType.Hand));
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double LowerValue
    {
        get => GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    public double UpperValue
    {
        get => GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    public bool IsRange
    {
        get => GetValue(IsRangeProperty);
        set => SetValue(IsRangeProperty, value);
    }

    public string ValueFormat
    {
        get => GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public IReadOnlyList<string>? ValueLabels
    {
        get => GetValue(ValueLabelsProperty);
        set => SetValue(ValueLabelsProperty, value);
    }

    private bool CanMove => Maximum > Minimum && IsEffectivelyEnabled;

    private double TrackWidth => Math.Max(0, Bounds.Width - 2 * Inset);

    private double XOf(double value) =>
        Inset + (Maximum > Minimum ? Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1) : 0) * TrackWidth;

    private double ValueAt(double x) =>
        TrackWidth <= 0 ? Minimum : Math.Round(Minimum + Math.Clamp((x - Inset) / TrackWidth, 0, 1) * (Maximum - Minimum));

    private string Label(double value)
    {
        var labels = ValueLabels;
        var index = (int)Math.Round(value - Minimum);
        return labels is not null && index >= 0 && index < labels.Count
            ? labels[index]
            : string.Format(CultureInfo.CurrentCulture, ValueFormat, Math.Round(value));
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 2 * Inset)
            return;

        // The track sits in the middle of the control; the hover label hangs below it, over whatever is there.
        var y = size.Height / 2;
        var track = new Rect(Inset, y - TrackHeight / 2, TrackWidth, TrackHeight);
        context.DrawRectangle(IsRange ? Track : TrackOn, null, new RoundedRect(track, TrackHeight / 2));

        var upper = XOf(UpperValue);
        if (IsRange)
        {
            var lower = XOf(LowerValue);
            context.DrawRectangle(TrackOn, null, new RoundedRect(new Rect(lower, track.Y, upper - lower, TrackHeight), TrackHeight / 2));
            DrawThumb(context, lower, y, IsFocused && !_upperActive);
        }
        DrawThumb(context, upper, y, IsFocused && (_upperActive || !IsRange));

        if (_hoverX is { } hover && CanMove)
            DrawHoverLabel(context, XOf(ValueAt(hover)), y + ThumbRadius + LabelGap);
    }

    private static void DrawThumb(DrawingContext context, double x, double y, bool focused)
    {
        var center = new Point(x, y);
        context.DrawEllipse(Thumb, ThumbEdge, center, ThumbRadius, ThumbRadius);
        if (focused)
            context.DrawEllipse(null, FocusRing, center, ThumbRadius + 3, ThumbRadius + 3);
    }

    private void DrawHoverLabel(DrawingContext context, double x, double top)
    {
        var text = new FormattedText(Label(ValueAt(_hoverX!.Value)), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Parse("avares://FuserSongManager/Assets/Fonts#Kumbh Sans"), FontStyle.Normal, FontWeight.SemiBold),
            14, LabelInk);
        var box = new Rect(x - text.Width / 2 - 6, top, text.Width + 12, text.Height + 4);
        context.DrawRectangle(LabelBack, null, new RoundedRect(box, 3));
        context.DrawText(text, new Point(box.X + 6, box.Y + 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!CanMove || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var x = e.GetPosition(this).X;
        // The nearer thumb; when they overlap, the side of the click decides.
        _upperActive = !IsRange || Math.Abs(x - XOf(UpperValue)) < Math.Abs(x - XOf(LowerValue))
            || (UpperValue <= LowerValue && x > XOf(UpperValue));
        Focus(NavigationMethod.Pointer);
        e.Pointer.Capture(this);
        MoveActiveTo(ValueAt(x));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        _hoverX = x;
        if (Equals(e.Pointer.Captured, this))
            MoveActiveTo(ValueAt(x));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Equals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverX = null;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!CanMove)
            return;
        var current = _upperActive || !IsRange ? UpperValue : LowerValue;
        double? target = e.Key switch
        {
            Key.Left or Key.Down => current - 1,
            Key.Right or Key.Up => current + 1,
            Key.PageDown => current - 10,
            Key.PageUp => current + 10,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (target is { } value)
        {
            MoveActiveTo(value);
            e.Handled = true;
        }
    }

    /// <summary>Moves the active thumb, never past the other one.</summary>
    private void MoveActiveTo(double value)
    {
        value = Math.Clamp(Math.Round(value), Minimum, Maximum);
        if (!IsRange)
            UpperValue = value;
        else if (_upperActive)
            UpperValue = Math.Max(value, LowerValue);
        else
            LowerValue = Math.Min(value, UpperValue);
    }
}
