using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FuserSongManager.UI.Controls;

/// <summary>
/// The hover colour of Fuser's buttons. It appears at once when <see cref="IsActive"/> turns on and, when it
/// turns off, sweeps away to the right with a rippling wave on its trailing edge. Placed behind a button's
/// content, inside a border that clips to the button's rounded corners.
/// </summary>
public sealed class SweepFill : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SweepFill, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SweepFill, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.Register<SweepFill, TimeSpan>(nameof(Duration), TimeSpan.FromMilliseconds(420));

    /// <summary>Half the height of the wave, in pixels.</summary>
    public static readonly StyledProperty<double> WaveAmplitudeProperty =
        AvaloniaProperty.Register<SweepFill, double>(nameof(WaveAmplitude), 4.5);

    /// <summary>Distance between two crests, in pixels.</summary>
    public static readonly StyledProperty<double> WaveLengthProperty =
        AvaloniaProperty.Register<SweepFill, double>(nameof(WaveLength), 18);

    // Time for the wave to travel one wave length along the edge.
    private const double RipplePeriodSeconds = 0.55;

    static SweepFill()
    {
        AffectsRender<SweepFill>(FillProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<SweepFill>(false);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public TimeSpan Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public double WaveAmplitude
    {
        get => GetValue(WaveAmplitudeProperty);
        set => SetValue(WaveAmplitudeProperty, value);
    }

    public double WaveLength
    {
        get => GetValue(WaveLengthProperty);
        set => SetValue(WaveLengthProperty, value);
    }

    // 0 = covering the whole control, 1 = gone off the right edge.
    private double _progress = 1;
    private TimeSpan? _leaveStarted;
    private bool _frameRequested;

    /// <summary>For tests: how far the colour has swept away (0 covering, 1 gone).</summary>
    public double Progress => _progress;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsActiveProperty)
            return;

        if (IsActive)
        {
            _progress = 0;
            _leaveStarted = null;
            InvalidateVisual();
        }
        else if (_progress < 1)
        {
            _leaveStarted = null;   // set on the first frame, from the frame clock
            RequestFrame();
        }
    }

    private void RequestFrame()
    {
        if (_frameRequested)
            return;
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            // Not on screen: nothing to animate.
            _progress = IsActive ? 0 : 1;
            return;
        }
        _frameRequested = true;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        _frameRequested = false;
        if (IsActive)
            return;

        _leaveStarted ??= now;
        var duration = Math.Max(1, Duration.TotalMilliseconds);
        var t = Math.Clamp((now - _leaveStarted.Value).TotalMilliseconds / duration, 0, 1);
        _progress = Ease(t);
        _phase = (now.TotalSeconds / RipplePeriodSeconds) % 1.0;
        InvalidateVisual();
        if (t < 1)
            RequestFrame();
    }

    private double _phase;

    /// <summary>Slow start, quick middle, soft landing (close to CSS cubic-bezier(.6,.02,.3,1)).</summary>
    private static double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _progress = IsActive ? 0 : 1;
        _frameRequested = false;
    }

    public override void Render(DrawingContext context)
    {
        var fill = Fill;
        var bounds = new Rect(Bounds.Size);
        if (fill is null || _progress >= 1 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (_progress <= 0)
        {
            context.DrawRectangle(fill, null, bounds);
            return;
        }

        // The brush is laid over the whole control so its gradient stays put while the edge moves.
        using (context.PushGeometryClip(EdgeGeometry(bounds.Width, bounds.Height)))
            context.DrawRectangle(fill, null, bounds);
    }

    private StreamGeometry EdgeGeometry(double width, double height)
    {
        var amplitude = WaveAmplitude;
        var wave = Math.Max(4, WaveLength);
        // The wave's middle travels from just left of the control (hidden) to past its right edge.
        var left = -amplitude + _progress * (width + 2 * amplitude);

        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(new Point(width + 1, -1), isFilled: true);
        g.LineTo(new Point(width + 1, height + 1));
        const double step = 2;
        for (var y = height + 1; y >= -1; y -= step)
            g.LineTo(new Point(left + amplitude * Math.Sin(2 * Math.PI * (y / wave + _phase)), y));
        g.EndFigure(isClosed: true);
        return geometry;
    }
}
