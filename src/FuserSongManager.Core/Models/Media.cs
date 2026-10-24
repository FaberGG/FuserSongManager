namespace FuserSongManager.Core.Models;

/// <summary>The four parts every Fuser song is made of. Order is the mixing and display order.</summary>
public enum StemKind
{
    Beat,
    Bass,
    Loop,
    Lead,
}

/// <summary>
/// The instrument the game shows for each part, as the game names it (<c>EInstrument::Drums</c> → "Drums"):
/// Drums, Guitar, Synth, Sampler, Strings, Horns, Vocal or None. Null means the part's asset has no value.
/// </summary>
public sealed record SongInstruments(string? Beat, string? Bass, string? Loop, string? Lead)
{
    /// <summary>For songs whose paks are not read (patched DLC) or whose parts could not be read.</summary>
    public static readonly SongInstruments Unknown = new(null, null, null, null);

    public string? For(StemKind kind) => kind switch
    {
        StemKind.Beat => Beat,
        StemKind.Bass => Bass,
        StemKind.Loop => Loop,
        StemKind.Lead => Lead,
        _ => null,
    };
}

/// <summary>One part of a song, ready to decode.</summary>
/// <param name="ZoneLabel">The keyzone the audio comes from (<c>Major</c>, <c>Minor</c>, <c>Unpitched</c>…).</param>
/// <param name="OggData">Plain Ogg Vorbis, already decrypted.</param>
public sealed record SongStem(StemKind Kind, string ZoneLabel, byte[] OggData);

/// <summary>The audio of a song: its parts, played together, give the full song (RF-PEN-13).</summary>
public sealed record SongAudio(string SongName, IReadOnlyList<SongStem> Stems);
