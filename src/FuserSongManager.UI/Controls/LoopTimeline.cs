using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace FuserSongManager.UI.Controls;

/// <summary>
/// The bar over the game's turntables: one mark per bar, a taller one at the start of each phrase, the part
/// already played filled in, and a playhead. Clicking or dragging on it moves the playhead.
/// </summary>
public sealed class LoopTimeline : Control
{
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<LoopTimeline, double>(nameof(Position), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<LoopTimeline, double>(nameof(Duration));

    /// <summary>Bars in the song; 0 when unknown, which draws the usual 32 as a placeholder.</summary>
    public static readonly StyledProperty<int> BarCountProperty =
        AvaloniaProperty.Register<LoopTimeline, int>(nameof(BarCount));

    public static readonly StyledProperty<int> BarsPerPhraseProperty =
        AvaloniaProperty.Register<LoopTimeline, int>(nameof(BarsPerPhrase), 8);

    /// <summary>Whether clicking moves the playhead (only once the song is loaded).</summary>
    public static readonly StyledProperty<bool> CanSeekProperty =
        AvaloniaProperty.Register<LoopTimeline, bool>(nameof(CanSeek));

    private const int PlaceholderBars = 32;
    private const double Radius = 6;
    private const double Inset = 8;

    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#8C0A0618"));
    private static readonly IBrush Played = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#269D84F6"), 0), new GradientStop(Color.Parse("#8C9D84F6"), 1) },
    };
    private static readonly IBrush Tick = new SolidColorBrush(Color.Parse("#59FFFFFF"));
    private static readonly IBrush PhraseTick = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush PlayedTick = Brushes.White;
    private static readonly IBrush Head = Brushes.White;
    private static readonly IBrush HeadGlow = new SolidColorBrush(Colors.White, 0.25);

    static LoopTimeline()
    {
        AffectsRender<LoopTimeline>(PositionProperty, DurationProperty, BarCountProperty, BarsPerPhraseProperty, CanSeekProperty);
    }

    public double Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public int BarCount
    {
        get => GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    public int BarsPerPhrase
    {
        get => GetValue(BarsPerPhraseProperty);
        set => SetValue(BarsPerPhraseProperty, value);
    }

    public bool CanSeek
    {
        get => GetValue(CanSeekProperty);
        set => SetValue(CanSeekProperty, value);
    }

    private double Fraction => Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 2 * Inset || size.Height <= 0)
            return;

        var track = new RoundedRect(new Rect(size), Radius);
        context.DrawRectangle(Track, null, track);

        var fraction = Fraction;
        var x = Inset + fraction * (size.Width - 2 * Inset);
        if (fraction > 0)
        {
            using (context.PushClip(track))
                context.DrawRectangle(Played, null, new Rect(0, 0, x, size.Height));
        }

        var bars = BarCount > 0 ? BarCount : PlaceholderBars;
        var perPhrase = Math.Max(1, BarsPerPhrase);
        var playedBars = Duration > 0 ? fraction * bars : -1;
        var bottom = size.Height - 5;
        for (var bar = 0; bar < bars; bar++)
        {
            var tx = Inset + bar * (size.Width - 2 * Inset) / bars;
            var phraseStart = bar % perPhrase == 0;
            var height = phraseStart ? size.Height * 0.55 : size.Height * 0.24;
            var brush = bar <= playedBars && fraction > 0 ? PlayedTick : phraseStart ? PhraseTick : Tick;
            context.DrawRectangle(brush, null, new RoundedRect(new Rect(tx - 1, bottom - height, 2, height), 1));
        }

        if (Duration > 0)
        {
            context.DrawRectangle(HeadGlow, null, new Rect(x - 4, 0, 8, size.Height));
            context.DrawRectangle(Head, null, new Rect(x - 1.5, 0, 3, size.Height));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!CanSeek || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Pointer.Capture(this);
        SeekTo(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (CanSeek && Equals(e.Pointer.Captured, this))
            SeekTo(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Equals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);
    }

    private void SeekTo(double x)
    {
        var width = Bounds.Width - 2 * Inset;
        if (width <= 0 || Duration <= 0)
            return;
        Position = Math.Clamp((x - Inset) / width, 0, 1) * Duration;
    }
}
