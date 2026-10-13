namespace FuserSongManager.Core.Models;

/// <summary>The four parts every Fuser song is made of. Order is the mixing and display order.</summary>
public enum StemKind
{
    Beat,
    Bass,
    Loop,
    Lead,
}

/// <summary>One part of a song, ready to decode.</summary>
/// <param name="ZoneLabel">The keyzone the audio comes from (<c>Major</c>, <c>Minor</c>, <c>Unpitched</c>…).</param>
/// <param name="OggData">Plain Ogg Vorbis, already decrypted.</param>
public sealed record SongStem(StemKind Kind, string ZoneLabel, byte[] OggData);

/// <summary>The audio of a song: its parts, played together, give the full song (RF-PEN-13).</summary>
public sealed record SongAudio(string SongName, IReadOnlyList<SongStem> Stems);
