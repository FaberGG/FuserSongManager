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
}
