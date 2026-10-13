using System.Text.Json;
using System.Text.Json.Serialization;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Infrastructure.Pak;

/// <summary>
/// Metadata of the former official DLC songs patched to load as customs. v1 could not parse
/// their paks and kept this list (extracted with FModel); it is embedded unchanged.
/// </summary>
public sealed class PatchedDlcCatalog
{
    private const string ResourceName = "patched_dlc_metadata.json";

    private readonly Dictionary<string, SongMetadata> _songs;

    private PatchedDlcCatalog(Dictionary<string, SongMetadata> songs) => _songs = songs;

    public int Count => _songs.Count;

    /// <param name="shortName">Pak name without <c>_P</c>, e.g. <c>adoreyou</c>.</param>
    public bool TryGet(string shortName, out SongMetadata metadata) => _songs.TryGetValue(shortName, out metadata!);

    public static PatchedDlcCatalog LoadEmbedded()
    {
        using var stream = typeof(PatchedDlcCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        return Load(stream);
    }

    public static PatchedDlcCatalog Load(Stream json)
    {
        var entries = JsonSerializer.Deserialize<List<Entry>>(json) ?? [];
        return new PatchedDlcCatalog(entries.ToDictionary(
            e => e.FileName,
            e => new SongMetadata(
                e.Title,
                e.Artist,
                e.Year,
                Enum.Parse<Genre>(e.Genre),
                Enum.Parse<MusicalKey>(e.Key),
                Enum.Parse<SongMode>(e.Mode),
                e.Bpm),
            StringComparer.OrdinalIgnoreCase));
    }

    private sealed record Entry(
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("song_name")] string Title,
        [property: JsonPropertyName("song_artist")] string Artist,
        [property: JsonPropertyName("song_bpm")] int Bpm,
        [property: JsonPropertyName("song_genre")] string Genre,
        [property: JsonPropertyName("song_key")] string Key,
        [property: JsonPropertyName("song_mode")] string Mode,
        [property: JsonPropertyName("song_year")] int Year);
}
