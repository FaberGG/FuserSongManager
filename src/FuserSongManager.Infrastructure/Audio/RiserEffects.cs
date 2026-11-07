using FuserSongManager.Core.Models;

namespace FuserSongManager.Infrastructure.Audio;

/// <summary>
/// What the song does while the riser comes in (see <see cref="RiserEffect"/>). The <see cref="TransitionMixer"/>
/// calls it for each stretch of audio it plays, so it works in real time and stays on the sample grid.
/// Times are frames since the riser started; negative ones do not exist: the stretch before it is <see cref="BeforeRiser"/>.
/// </summary>
public abstract class RiserEffectProcessor
{
    /// <summary>
    /// The song is still playing, waiting for the bar line, and <paramref name="block"/> holds what is about to be
    /// heard. An effect that needs to start before the riser (the echo catches the last beat) adds its sound here.
    /// </summary>
    /// <param name="framesUntilRiser">Frames until the riser starts, counted from the first frame of the block.</param>
    public virtual void BeforeRiser(float[] block, int offset, int frames, long framesUntilRiser)
    {
    }

    /// <summary>Called before the song is read for the next stretch: effects that work on the parts set their volumes here.</summary>
    public virtual void SetStemGains(StemMixer song, long riserFrame, int frames)
    {
    }

    /// <summary>False once the effect has no use for the song: it is not read, and <see cref="Process"/> gets silence.</summary>
    public virtual bool NeedsSong(long riserFrame) => true;

    /// <summary>
    /// <paramref name="block"/> holds the song for the stretch (or silence); on return it holds what is heard of the song:
    /// the effect's own sound, which may go on after the song is no longer needed.
    /// </summary>
    public abstract void Process(float[] block, int frames, long riserFrame);
}

/// <summary>Builds the processor of each <see cref="RiserEffect"/>, as heard in the listening tests.</summary>
public static class RiserEffects
{
    /// <summary>The processor for <paramref name="effect"/>, or null for <see cref="RiserEffect.FadeOut"/>, which the mixer does itself.</summary>
    public static RiserEffectProcessor? Create(RiserEffect effect, int sampleRate, long barFrames)
    {
        var beat = barFrames / 4.0;
        return effect switch
        {
            RiserEffect.LowPassFilter => new LowPassSweep(sampleRate, barFrames),
            RiserEffect.EchoOut => new Echo(sampleRate, beat, delayBeats: 0.75, feedback: 0.6, damp: 3500, wet: 0.8),
            RiserEffect.DubEcho => new Echo(sampleRate, beat, delayBeats: 1.0, feedback: 0.75, damp: 2200, wet: 0.8),
            RiserEffect.TranceGate => new Gate(sampleRate, beat, barFrames),
            RiserEffect.StripDown => new StemGains(barFrames, StripDownGain),
            RiserEffect.RhythmHold => new StemGains(barFrames, RhythmHoldGain),
            _ => null,
        };
    }

    /// <summary>1 at the start, eased down to 0 between <paramref name="from"/> and <paramref name="to"/> (same unit).</summary>
    internal static float Fade(double at, double from, double to)
    {
        if (at <= from) return 1f;
        if (at >= to) return 0f;
        return (float)(0.5 * (1 + Math.Cos(Math.PI * (at - from) / (to - from))));
    }

    // The parts leave one per bar: lead, loop, bass, and the beat last (bars since the riser started).
    static float StripDownGain(StemKind part, double bars) => part switch
    {
        StemKind.Lead => Fade(bars, 0, 0.25),
        StemKind.Loop => Fade(bars, 1, 1.25),
        StemKind.Bass => Fade(bars, 2, 2.25),
        _ => Fade(bars, 3, 4),
    };

    // Lead and loop leave in the first bar; the rhythm section holds and goes out from the middle of bar 3.
    static float RhythmHoldGain(StemKind part, double bars) =>
        part is StemKind.Beat or StemKind.Bass ? Fade(bars, 2.5, 4) : Fade(bars, 0, 1);

    /// <summary>A two-pole filter whose cutoff can change as it runs.</summary>
    internal sealed class Biquad
    {
        double _a0, _a1, _a2, _b1, _b2, _z1, _z2;

        public void SetLowPass(double cutoff, double q, int sampleRate)
        {
            cutoff = Math.Clamp(cutoff, 20, sampleRate * 0.45);
            var w = 2 * Math.PI * cutoff / sampleRate;
            var cos = Math.Cos(w);
            var alpha = Math.Sin(w) / (2 * q);
            var a0n = 1 + alpha;
            _a0 = (1 - cos) / 2 / a0n;
            _a1 = (1 - cos) / a0n;
            _a2 = (1 - cos) / 2 / a0n;
            _b1 = -2 * cos / a0n;
            _b2 = (1 - alpha) / a0n;
        }

        public float Process(float x)
        {
            var y = _a0 * x + _z1;
            _z1 = _a1 * x - _b1 * y + _z2;
            _z2 = _a2 * x - _b2 * y;
            return (float)y;
        }
    }

    /// <summary>Low Pass Filter: the cutoff sweeps from 18 kHz to 200 Hz over two bars and the song fades between bar 1.5 and 2.5.</summary>
    sealed class LowPassSweep(int sampleRate, long barFrames) : RiserEffectProcessor
    {
        const double Start = 18000, End = 200;
        readonly Biquad _left = new(), _right = new();

        public override bool NeedsSong(long riserFrame) => riserFrame < 2.5 * barFrames;

        public override void Process(float[] block, int frames, long riserFrame)
        {
            for (var f = 0; f < frames; f++)
            {
                var n = riserFrame + f;
                if (n % 32 == 0)
                {
                    // An exponential sweep sounds even to the ear.
                    var x = Math.Min(1.0, n / (2.0 * barFrames));
                    var cutoff = Start * Math.Pow(End / Start, x);
                    _left.SetLowPass(cutoff, 1.1, sampleRate);
                    _right.SetLowPass(cutoff, 1.1, sampleRate);
                }
                var gain = Fade(n, 1.5 * barFrames, 2.5 * barFrames);
                block[f * 2] = _left.Process(block[f * 2]) * gain;
                block[f * 2 + 1] = _right.Process(block[f * 2 + 1]) * gain;
            }
        }
    }

    /// <summary>
    /// Echo Out and Dub Echo: the song is cut as the riser starts, but its last beat was thrown into a feedback delay
    /// (the send opens over the beat before), and that rings on under the riser, getting darker as it repeats.
    /// </summary>
    sealed class Echo : RiserEffectProcessor
    {
        readonly double _beat, _wet;
        readonly float _feedback;
        readonly float[] _bufferLeft, _bufferRight;
        readonly Biquad _dampLeft = new(), _dampRight = new();
        int _index;

        public Echo(int sampleRate, double beatFrames, double delayBeats, double feedback, double damp, double wet)
        {
            _beat = beatFrames;
            _feedback = (float)feedback;
            _wet = wet;
            var length = Math.Max(1, (int)(delayBeats * beatFrames));
            _bufferLeft = new float[length];
            _bufferRight = new float[length];
            _dampLeft.SetLowPass(damp, 0.7, sampleRate);
            _dampRight.SetLowPass(damp, 0.7, sampleRate);
        }

        public override bool NeedsSong(long riserFrame) => false;

        public override void BeforeRiser(float[] block, int offset, int frames, long framesUntilRiser)
        {
            for (var f = 0; f < frames; f++)
            {
                var send = (float)Math.Clamp((_beat - (framesUntilRiser - f)) / _beat, 0, 1);
                var (l, r) = Step(block[offset + f * 2] * send, block[offset + f * 2 + 1] * send);
                block[offset + f * 2] += l;
                block[offset + f * 2 + 1] += r;
            }
        }

        public override void Process(float[] block, int frames, long riserFrame)
        {
            for (var f = 0; f < frames; f++)
                (block[f * 2], block[f * 2 + 1]) = Step(0, 0);
        }

        // Writes the input into the delay with the feedback and returns the echo that comes out.
        (float, float) Step(float inLeft, float inRight)
        {
            var left = _bufferLeft[_index];
            var right = _bufferRight[_index];
            _bufferLeft[_index] = inLeft + _dampLeft.Process(left) * _feedback;
            _bufferRight[_index] = inRight + _dampRight.Process(right) * _feedback;
            _index = (_index + 1) % _bufferLeft.Length;
            return ((float)(left * _wet), (float)(right * _wet));
        }
    }

    /// <summary>Trance Gate: the song is chopped by a gate that goes 1/8, 1/16, 1/32, 1/32 notes bar by bar, fading from bar 1 to 4.</summary>
    sealed class Gate(int sampleRate, double beatFrames, long barFrames) : RiserEffectProcessor
    {
        static readonly double[] PeriodBeats = [0.5, 0.25, 0.125, 0.125];
        static readonly double[] Duty = [0.6, 0.5, 0.5, 0.4];
        readonly double _ramp = 0.002 * sampleRate;   // 2 ms edges, so it does not click

        public override void Process(float[] block, int frames, long riserFrame)
        {
            for (var f = 0; f < frames; f++)
            {
                var n = riserFrame + f;
                var bar = (int)Math.Min(3, n / barFrames);
                var period = PeriodBeats[bar] * beatFrames;
                var phase = n - Math.Floor(n / period) * period;
                var open = period * Duty[bar];
                var gate = (float)Math.Max(0, Math.Min(1.0, Math.Min(phase / _ramp, (open - phase) / _ramp)));
                gate *= Fade(n, barFrames, 4.0 * barFrames);
                block[f * 2] *= gate;
                block[f * 2 + 1] *= gate;
            }
        }
    }

    /// <summary>Strip Down and Rhythm Hold: each part has its own volume over time; the song is not otherwise touched.</summary>
    sealed class StemGains(long barFrames, Func<StemKind, double, float> gain) : RiserEffectProcessor
    {
        public override void SetStemGains(StemMixer song, long riserFrame, int frames)
        {
            // The volume the part should have at the end of this stretch; the mixer ramps to it.
            var bars = (riserFrame + frames) / (double)barFrames;
            foreach (var part in Enum.GetValues<StemKind>())
                song.SetGain(part, gain(part, bars));
        }

        public override void Process(float[] block, int frames, long riserFrame)
        {
        }
    }
}
