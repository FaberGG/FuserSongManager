using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FuserSongManager.Core.Models;

namespace FuserSongManager.UI;

/// <summary>Converters used by the views to show parts and instruments the way the game does.</summary>
public static class Converters
{
    /// <summary>A resource key from <see cref="Display.InstrumentIconKey"/> → its icon geometry (null if none).</summary>
    public static readonly IValueConverter IconGeometry = new FuncValueConverter<string?, Geometry?>(key =>
        key is not null && Application.Current?.TryGetResource(key, null, out var value) == true ? value as Geometry : null);

    /// <summary>The game's instrument name ("Drums", "Synth"…) → its icon geometry (null if none).</summary>
    public static readonly IValueConverter InstrumentGeometry = new FuncValueConverter<string?, Geometry?>(instrument =>
        IconGeometry.Convert(Display.InstrumentIconKey(instrument), typeof(Geometry), null, CultureInfo.InvariantCulture) as Geometry);

    /// <summary>A part → its colour: beat cyan, bass green, loop yellow, lead red, like the game's discs.</summary>
    public static readonly IValueConverter PartBrush = new FuncValueConverter<StemKind, IBrush?>(kind =>
        Application.Current?.TryGetResource($"Fsm.Part.{kind}", null, out var value) == true ? value as IBrush : Brushes.White);
}
