using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FuserSongManager.UI.Controls;

/// <summary>
/// The background of the app: the festival sky at night, out of focus, as the game shows it behind its crate.
/// Drawn from gradients and soft stars rather than a picture, so it fits any window size.
/// </summary>
public sealed class NightSky : Control
{
    private static readonly IBrush Sky = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#070A1E"), 0),
            new GradientStop(Color.Parse("#0F1233"), 0.38),
            new GradientStop(Color.Parse("#1D1A47"), 0.70),
            new GradientStop(Color.Parse("#33265F"), 1),
        },
    };

    // Soft glows: the stage lights below the horizon, and a bit of haze up high.
    private static readonly (double X, double Y, double RadiusX, double RadiusY, Color Color)[] Glows =
    [
        (0.72, 1.12, 0.62, 0.52, Color.Parse("#8C9646D2")),
        (0.08, 1.08, 0.52, 0.46, Color.Parse("#613C60DC")),
        (0.45, 0.00, 0.40, 0.34, Color.Parse("#59282878")),
    ];

    private readonly record struct Star(double X, double Y, double Radius, double Alpha);

    private static readonly Star[] Stars = MakeStars();

    private static Star[] MakeStars()
    {
        // Fixed seed: the same sky every time.
        var random = new Random(1987);
        var stars = new Star[170];
        for (var i = 0; i < stars.Length; i++)
        {
            // Fewer stars near the horizon, where the glow washes them out.
            var y = Math.Pow(random.NextDouble(), 1.6) * 0.9;
            stars[i] = new Star(random.NextDouble(), y, 0.5 + random.NextDouble() * 0.9, 0.25 + random.NextDouble() * 0.6);
        }
        return stars;
    }

    static NightSky()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<NightSky>(false);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;
        var bounds = new Rect(size);
        context.DrawRectangle(Sky, null, bounds);

        foreach (var (x, y, rx, ry, color) in Glows)
        {
            var center = new Point(x * size.Width, y * size.Height);
            var radii = new Size(rx * size.Width, ry * size.Height);
            var glow = new RadialGradientBrush
            {
                Center = new RelativePoint(center, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(center, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(radii.Width, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(radii.Height, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(color, 0),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
                },
            };
            context.DrawRectangle(glow, null, bounds);
        }

        // Each star is a dot with a faint halo: what a blurred photo of the sky looks like.
        foreach (var star in Stars)
        {
            var center = new Point(star.X * size.Width, star.Y * size.Height);
            context.DrawEllipse(new SolidColorBrush(Colors.White, star.Alpha * 0.18), null, center, star.Radius * 3.2, star.Radius * 3.2);
            context.DrawEllipse(new SolidColorBrush(Colors.White, star.Alpha), null, center, star.Radius, star.Radius);
        }
    }
}
