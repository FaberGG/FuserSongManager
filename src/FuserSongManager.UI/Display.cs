using FuserSongManager.Core.Models;

namespace FuserSongManager.UI;

/// <summary>How enum values and ratings are shown in the UI.</summary>
public static class Display
{
    public static string Genre(Genre? genre) => genre switch
    {
        null => "",
        Core.Models.Genre.LatinAndCaribbean => "Latin & Caribbean",
        Core.Models.Genre.RnB => "R&B",
        Core.Models.Genre.HipHop => "Hip Hop",
        _ => genre.Value.ToString(),
    };

    /// <summary>Flats with the ♭ sign, as the game shows them (Ab → A♭).</summary>
    public static string Key(MusicalKey? key) => key switch
    {
        null => "",
        _ => key.Value.ToString().Replace("b", "♭"),
    };

    public static string Mode(SongMode? mode) => mode?.ToString() ?? "";

    public static string Location(SongLocation location) => location == SongLocation.Enabled ? "Enabled" : "Disabled";

    public static string Stars(int rating)
    {
        rating = Math.Clamp(rating, 0, 5);
        return new string('★', rating) + new string('☆', 5 - rating);
    }

    public static string ReadStatus(ReadStatus status) => status switch
    {
        Core.Models.ReadStatus.Ok => "Read correctly",
        Core.Models.ReadStatus.Partial => "Partially read",
        _ => "Could not be read",
    };

    /// <summary>Why a song is flagged in the list (RF-UI-04), or null if it has no problem.</summary>
    public static string? Problem(Song song)
    {
        var problems = new List<string>(2);
        if (song.ReadStatus == Core.Models.ReadStatus.Failed)
            problems.Add("The pak could not be read" + (song.ReadError is null ? "." : $": {song.ReadError}"));
        else if (song.ReadStatus == Core.Models.ReadStatus.Partial)
            problems.Add("Some fields could not be read" + (song.ReadError is null ? "." : $" ({song.ReadError})."));
        if (!song.HasSignature)
            problems.Add($"{song.FileName}.sig is missing: the game will not load this song.");
        return problems.Count == 0 ? null : string.Join(Environment.NewLine, problems);
    }

    public static string Songs(int count) => count == 1 ? "1 song" : $"{count} songs";

    /// <summary>
    /// The instrument of a part as the game labels it. The bass part's guitar is a bass guitar
    /// (its <c>SubInstrument</c> is always <c>BassGuitar</c>). Null or "None" means the part has no instrument.
    /// </summary>
    public static string Instrument(StemKind part, string? instrument) => instrument switch
    {
        null or "" or "None" => "No instrument",
        "Guitar" when part == StemKind.Bass => "Bass guitar",
        "Vocal" => "Vocals",
        "Keys" or "Piano" => "Keys",
        _ => SplitWords(instrument),
    };

    /// <summary>
    /// Resource key of the icon for an instrument (see Themes/InstrumentIcons.axaml), or null for none. The game's
    /// icon set: drums, guitar (also the bass guitar), synth (piano keys), sampler (a turntable), horns (a trumpet),
    /// strings (a violin) and vocals (a microphone).
    /// </summary>
    public static string? InstrumentIconKey(string? instrument) => instrument switch
    {
        "Drums" => "Icon.Drums",
        "Guitar" or "BassGuitar" => "Icon.Guitar",
        "Synth" or "Keys" or "Piano" => "Icon.Synth",
        "Sampler" => "Icon.Sampler",
        "Horns" or "Brass" => "Icon.Horns",
        "Strings" => "Icon.Strings",
        "Vocal" or "Vocals" => "Icon.Vocals",
        _ => null,
    };

    /// <summary>"BassGuitar" → "Bass guitar".</summary>
    private static string SplitWords(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
}
