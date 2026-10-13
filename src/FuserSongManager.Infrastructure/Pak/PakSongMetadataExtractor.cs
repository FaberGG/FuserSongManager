using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace FuserSongManager.Infrastructure.Pak;

/// <summary>
/// The seven v1 fields as they come out of the pak, with enums as their game names
/// ("Rock", "Ab", "Minor"). Missing lists every field that could not be read.
/// </summary>
public sealed record PakSongMetadata(
    string ShortName,
    string? Title,
    string? Artist,
    int? Year,
    string? Genre,
    string? Key,
    string? Mode,
    int? Bpm,
    IReadOnlyList<string> Missing);

public static class PakSongMetadataExtractor
{
    public static PakSongMetadata Read(string pakPath, EGame game = FuserPak.DefaultGame)
    {
        using var provider = FuserPak.Open(pakPath, game);
        return Read(provider, FuserPak.ResolveSongName(provider, pakPath));
    }

    public static PakSongMetadata Read(IFileProvider provider, string shortName)
    {
        var missing = new List<string>();
        var songSet = FuserPak.LoadFirstExport(provider, FuserPak.SongSetAssetPath(shortName));
        var bassCel = FuserPak.LoadFirstExport(provider, FuserPak.BassCelAssetPath(shortName));

        var genre = EnumValue(songSet, "Genre", missing);
        // Same fallback as v1 for genres the manager does not know.
        genre = genre switch { "Num" => "Indie", "None" => "Other", _ => genre };

        return new PakSongMetadata(
            shortName,
            Title: Text(bassCel, "Title", missing),
            Artist: Text(songSet, "Artist", missing),
            Year: Int(songSet, "Year", missing),
            Genre: genre,
            Key: EnumValue(bassCel, "Key", missing),
            Mode: EnumValue(bassCel, "Mode", missing),
            Bpm: Int(bassCel, "BPM", missing),
            missing);
    }

    private static string? Text(UObject obj, string name, List<string> missing)
    {
        if (obj.TryGetValue(out FText text, name))
            return text.Text;
        missing.Add(name);
        return null;
    }

    private static int? Int(UObject obj, string name, List<string> missing)
    {
        if (obj.TryGetValue(out int value, name))
            return value;
        missing.Add(name);
        return null;
    }

    /// <summary>"EGenre::Rock" → "Rock".</summary>
    private static string? EnumValue(UObject obj, string name, List<string> missing)
    {
        if (obj.TryGetValue(out FName value, name))
        {
            var text = value.Text;
            var separator = text.LastIndexOf("::", StringComparison.Ordinal);
            return separator >= 0 ? text[(separator + 2)..] : text;
        }
        missing.Add(name);
        return null;
    }
}
