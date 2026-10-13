using CUE4Parse.UE4.Versions;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Infrastructure.Pak;

/// <summary>Reads song metadata with CUE4Parse, or from the patched DLC catalog for those songs (as v1 does).</summary>
public sealed class CueSongMetadataReader(PatchedDlcCatalog patchedDlc, EGame game = FuserPak.DefaultGame) : ISongMetadataReader
{
    public SongReadResult Read(string pakPath)
    {
        var shortName = FuserPak.ShortName(pakPath);
        if (patchedDlc.TryGet(shortName, out var catalogMetadata))
            return new SongReadResult(catalogMetadata, ReadStatus.Ok, null, MetadataSource.PatchedDlcCatalog);

        PakSongMetadata raw;
        try
        {
            raw = PakSongMetadataExtractor.Read(pakPath, game);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
        {
            return SongReadResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            return SongReadResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }

        var metadata = new SongMetadata(
            raw.Title,
            raw.Artist,
            raw.Year,
            raw.Genre is null ? null : ParseOr(raw.Genre, Genre.Other),
            raw.Key is null ? null : ParseOrNull<MusicalKey>(raw.Key),
            raw.Mode is null ? null : ParseOrNull<SongMode>(raw.Mode),
            raw.Bpm);

        return raw.Missing.Count == 0
            ? new SongReadResult(metadata, ReadStatus.Ok, null)
            : new SongReadResult(metadata, ReadStatus.Partial, $"Missing: {string.Join(", ", raw.Missing)}");
    }

    // Unknown genres fall back to Other instead of failing (RF-MET-05).
    private static T ParseOr<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    // The game uses "Num" for "no value"; that and anything unknown map to null.
    private static T? ParseOrNull<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
}
