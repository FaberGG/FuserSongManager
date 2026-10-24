using Avalonia;
using Avalonia.Controls;

namespace FuserSongManager.UI.Controls;

/// <summary>
/// One row of square tiles: the width is split into <see cref="Columns"/> equal cells with <see cref="Spacing"/>
/// between them, and each tile is as tall as it is wide. A short last row keeps the same cell size.
/// </summary>
public sealed class TileRowPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<TileRowPanel, int>(nameof(Columns), 6);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TileRowPanel, double>(nameof(Spacing), 8);

    static TileRowPanel()
    {
        AffectsMeasure<TileRowPanel>(ColumnsProperty, SpacingProperty);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private double CellSize(double width)
    {
        var columns = Math.Max(1, Columns);
        return double.IsInfinity(width) ? 180 : Math.Max(0, (width - Spacing * (columns - 1)) / columns);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var cell = CellSize(availableSize.Width);
        foreach (var child in Children)
            child.Measure(new Size(cell, cell));
        var width = double.IsInfinity(availableSize.Width) ? Math.Max(1, Columns) * (cell + Spacing) - Spacing : availableSize.Width;
        return new Size(width, Children.Count == 0 ? 0 : cell);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cell = CellSize(finalSize.Width);
        var columns = Math.Max(1, Columns);
        for (var i = 0; i < Children.Count; i++)
        {
            var column = i % columns;
            Children[i].Arrange(new Rect(column * (cell + Spacing), 0, cell, cell));
        }
        return finalSize;
    }
}
