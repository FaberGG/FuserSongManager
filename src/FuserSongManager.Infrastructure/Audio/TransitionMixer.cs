namespace FuserSongManager.Infrastructure.Audio;

public enum RiserPhase
{
    None,
    /// <summary>Asked for, but the song plays on until the end of its bar.</summary>
    Waiting,
    /// <summary>The riser is building up; the song fades under it, or is silent.</summary>
    Playing,
    /// <summary>The song has started again; what is left of the riser rings out under it and fades away.</summary>
    Tail,
}

/// <summary>When everything happens, in frames at the song's rate.</summary>
/// <param name="WaitFrames">Song frames to play before the riser starts (the rest of the current bar).</param>
/// <param name="FadeFrames">The song fades out over this many frames from the start of the riser.</param>
/// <param name="HandOverFrames">The song starts again from its beginning once the riser has played this long.</param>
/// <param name="TailFrames">What is left of the riser after that rings out for at most this long, fading away.</param>
/// <param name="SongAudible">False when the song is not playing: only the riser is heard, then the song starts.</param>
public readonly record struct RiserPlan(long WaitFrames, long FadeFrames, long HandOverFrames, long TailFrames, bool SongAudible);

/// <summary>
/// The song's mixer with the riser transition on top, as one stream so the hand-over is exact to the sample:
/// the song plays until the riser is due, the riser starts and the song fades out under it, and in the very
/// buffer in which the riser's build-up ends the song starts again from its beginning, with no gap. The tail of the
/// riser file (the reverb after its last bar, and any padding) rings out under the song and fades away.
/// Independent of any audio device so it can be tested; <see cref="NAudioStemPlayer"/> feeds it to the sound card.
/// Not thread-safe by itself: the player serialises everything.
/// </summary>
public sealed class TransitionMixer(StemMixer song) : IDisposable
{
    private StemMixer? _riser;
    private RiserPlan _plan;
    private long _waitFrames;       // song frames left before the riser starts
    private long _handOver;         // riser frames after which the song starts again
    private long _riserFrames;      // riser frames played so far (before the hand-over)
    private long _tailFrames;       // riser frames played since the hand-over
    private bool _inTail;
    private float[] _scratch = [];

    public StemMixer Song { get; } = song;

    /// <summary>True from <see cref="StartRiser"/> until the song takes over; the tail is not counted.</summary>
    public bool IsRiserActive => Phase is RiserPhase.Waiting or RiserPhase.Playing;

    public RiserPhase Phase => _riser is null ? RiserPhase.None : _inTail ? RiserPhase.Tail : _waitFrames > 0 ? RiserPhase.Waiting : RiserPhase.Playing;

    /// <summary>Schedules <paramref name="riser"/>; it replaces any riser still active.</summary>
    public void StartRiser(StemMixer riser, RiserPlan plan)
    {
        Cancel();
        _riser = riser;
        _plan = plan with
        {
            WaitFrames = Math.Max(0, plan.WaitFrames),
            FadeFrames = Math.Clamp(plan.FadeFrames, 0, riser.LengthFrames),
        };
        _waitFrames = _plan.WaitFrames;
        _handOver = Math.Clamp(plan.HandOverFrames, 0, riser.LengthFrames);
        _riserFrames = 0;
        _tailFrames = 0;
        _inTail = false;
    }

    /// <summary>Drops the riser, tail included, and leaves the song as it is (the user moved the playhead or stopped).</summary>
    public void Cancel()
    {
        _riser?.Dispose();
        _riser = null;
        _waitFrames = 0;
        _inTail = false;
    }

    /// <summary>
    /// Ends the riser now. Before it started that just cancels it and the song goes on; once it sounds, the song
    /// starts from its beginning, as it does when the riser's last bar ends, and the riser is cut.
    /// </summary>
    public void Skip()
    {
        var sounding = Phase == RiserPhase.Playing;
        Cancel();
        if (sounding)
            Song.Seek(0);
    }

    /// <summary>The riser's build-up is over: the song starts again and the rest of the riser becomes its tail.</summary>
    private void BeginTail()
    {
        Song.Seek(0);
        if (_plan.TailFrames <= 0 || _handOver >= _riser!.LengthFrames)
        {
            Cancel();
            return;
        }
        _inTail = true;
        _tailFrames = 0;
    }

    /// <summary>Gain of something fading out, <paramref name="frame"/> frames in: 1 at the start, 0 at the end, eased so it neither jumps nor clicks.</summary>
    public static float FadeGain(long frame, long fadeFrames) =>
        frame >= fadeFrames ? 0f : (float)(0.5 * (1 + Math.Cos(Math.PI * frame / fadeFrames)));

    /// <summary>
    /// Fills <paramref name="buffer"/> with interleaved stereo; returns fewer than asked (0 at the end) only when the
    /// song ends and is not looping.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var wanted = count - total;
            int read;
            if (_riser is null)
            {
                read = Song.Read(buffer, offset + total, wanted);
            }
            else if (_waitFrames > 0)
            {
                var frames = (int)Math.Min(wanted / StemMixer.OutputChannels, _waitFrames);
                read = Song.Read(buffer, offset + total, frames * StemMixer.OutputChannels);
                _waitFrames = read < frames * StemMixer.OutputChannels ? 0 : _waitFrames - read / StemMixer.OutputChannels;
            }
            else if (!_inTail)
            {
                read = ReadRiser(buffer, offset + total, wanted);
                if (read < 0)
                    continue;   // the song took over: read on from it in this same buffer
            }
            else
            {
                read = ReadTail(buffer, offset + total, wanted);
            }

            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    /// <summary>The riser plus the fading song. Returns -1 when this call only moved on to the tail.</summary>
    private int ReadRiser(float[] buffer, int offset, int count)
    {
        var remaining = _handOver - _riserFrames;
        if (remaining <= 0)
        {
            BeginTail();
            return -1;
        }

        var read = _riser!.Read(buffer, offset, (int)Math.Min(count, remaining * StemMixer.OutputChannels));
        if (read == 0)
        {
            BeginTail();   // the riser file ended before its planned end
            return -1;
        }

        if (_plan.SongAudible && _riserFrames < _plan.FadeFrames)
        {
            if (_scratch.Length < read)
                _scratch = new float[read];
            var song = Song.Read(_scratch, 0, read);
            var frames = read / StemMixer.OutputChannels;
            for (var f = 0; f < frames && f * StemMixer.OutputChannels < song; f++)
            {
                var gain = FadeGain(_riserFrames + f, _plan.FadeFrames);
                for (var c = 0; c < StemMixer.OutputChannels; c++)
                {
                    var i = f * StemMixer.OutputChannels + c;
                    buffer[offset + i] = Math.Clamp(buffer[offset + i] + _scratch[i] * gain, -1f, 1f);
                }
            }
        }

        _riserFrames += read / StemMixer.OutputChannels;
        return read;
    }

    /// <summary>The song at full volume with the end of the riser fading out over it.</summary>
    private int ReadTail(float[] buffer, int offset, int count)
    {
        var read = Song.Read(buffer, offset, count);
        if (read == 0)
            return 0;

        if (_scratch.Length < read)
            _scratch = new float[read];
        var tail = _riser!.Read(_scratch, 0, read);
        var frames = tail / StemMixer.OutputChannels;
        for (var f = 0; f < frames; f++)
        {
            var gain = FadeGain(_tailFrames + f, _plan.TailFrames);
            for (var c = 0; c < StemMixer.OutputChannels; c++)
            {
                var i = f * StemMixer.OutputChannels + c;
                buffer[offset + i] = Math.Clamp(buffer[offset + i] + _scratch[i] * gain, -1f, 1f);
            }
        }
        _tailFrames += frames;
        if (tail < read || _tailFrames >= _plan.TailFrames)
            Cancel();   // nothing left of the riser, or it has faded away
        return read;
    }

    public void Dispose() => Cancel();
}
