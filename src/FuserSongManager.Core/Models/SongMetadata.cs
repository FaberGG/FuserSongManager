namespace FuserSongManager.Core.Models;

/// <summary>The seven fields the game stores for a song. Null means the value could not be read.</summary>
public sealed record SongMetadata(
    string? Title,
    string? Artist,
    int? Year,
    Genre? Genre,
    MusicalKey? Key,
    SongMode? Mode,
    int? Bpm)
{
    public static readonly SongMetadata Empty = new(null, null, null, null, null, null, null);
}

/// <summary>Outcome of reading one pak. A failed read still produces a result so the song stays in the library.</summary>
public sealed record SongReadResult(
    SongMetadata Metadata,
    ReadStatus Status,
    string? Error,
    MetadataSource Source = MetadataSource.Pak)
{
    public static SongReadResult Failed(string error) => new(SongMetadata.Empty, ReadStatus.Failed, error);
}
