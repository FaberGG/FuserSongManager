namespace FuserSongManager.Core.Models;

/// <summary>
/// What the song does while the riser comes in, named as DJs call these moves. The riser itself always sounds the
/// same; this is how the song gives way to it.
/// </summary>
public enum RiserEffect
{
    /// <summary>The song fades out over two bars.</summary>
    FadeOut,

    /// <summary>A low-pass filter closes over two bars: the song gets muffled until only the bass is left, then it is gone.</summary>
    LowPassFilter,

    /// <summary>The song is cut as the riser starts and its last beat rings out in a dotted-eighth echo.</summary>
    EchoOut,

    /// <summary>Like Echo Out but with a long, dark quarter-note echo that takes much longer to die away.</summary>
    DubEcho,

    /// <summary>A gate chops the song rhythmically, faster every bar (1/8, 1/16, 1/32), while it fades.</summary>
    TranceGate,

    /// <summary>The parts leave one per bar: lead, loop, bass and, last, the beat.</summary>
    StripDown,

    /// <summary>The lead and loop leave in the first bar; bass and beat hold the groove and go out in the second half.</summary>
    RhythmHold,
}
