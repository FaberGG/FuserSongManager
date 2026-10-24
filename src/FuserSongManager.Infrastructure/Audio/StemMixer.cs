using FuserSongManager.Core.Models;

namespace FuserSongManager.Infrastructure.Audio;

/// <summary>A decoded stream of interleaved float samples.</summary>
public interface IStemSource : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    long LengthFrames { get; }
    long PositionFrames { get; set; }

    /// <summary>Interleaved samples; returns fewer than asked (0 at the end).</summary>
    int Read(float[] buffer, int offset, int count);
}

/// <summary>
/// Sums the parts of a song into one stereo stream, with a mute switch per part. Independent of any audio
/// device so it can be tested; <see cref="NAudioStemPlayer"/> feeds it to the sound card.
/// Not thread-safe by itself: the player serialises reads and seeks.
/// </summary>
public sealed class StemMixer : IDisposable
{
    public const int OutputChannels = 2;

    // Four full-scale parts would clip; the game also lowers each part (its patches use about -3 dB).
    private const float PartGain = 0.6f;

    private readonly IReadOnlyList<(StemKind Kind, IStemSource Source)> _stems;
    private readonly HashSet<StemKind> _muted = [];
    private float[] _scratch = [];

    public StemMixer(IReadOnlyList<(StemKind Kind, IStemSource Source)> stems)
    {
        if (stems.Count == 0)
            throw new ArgumentException("There is nothing to play.", nameof(stems));
        SampleRate = stems[0].Source.SampleRate;
        var mismatched = stems.FirstOrDefault(s => s.Source.SampleRate != SampleRate || s.Source.Channels is < 1 or > 2);
        if (mismatched.Source is not null)
            throw new NotSupportedException(
                $"The {mismatched.Kind} part is {mismatched.Source.SampleRate} Hz with {mismatched.Source.Channels} channels; all parts must share the sample rate and be mono or stereo.");
        _stems = stems;
        LengthFrames = stems.Max(s => s.Source.LengthFrames);
    }

    public int SampleRate { get; }
    public long LengthFrames { get; }
    public long PositionFrames { get; private set; }
    public IEnumerable<StemKind> Kinds => _stems.Select(s => s.Kind);

    public void SetMuted(StemKind kind, bool muted)
    {
        if (muted) _muted.Add(kind);
        else _muted.Remove(kind);
    }

    public bool IsMuted(StemKind kind) => _muted.Contains(kind);

    /// <summary>
    /// Starts again from the beginning instead of ending, as the game does: a Fuser song is four phrases of
    /// eight bars that are made to repeat seamlessly.
    /// </summary>
    public bool Loop { get; set; }

    public void Seek(long frame)
    {
        frame = Math.Clamp(frame, 0, LengthFrames);
        foreach (var (_, source) in _stems)
            source.PositionFrames = Math.Min(frame, source.LengthFrames);
        PositionFrames = frame;
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with interleaved stereo; returns 0 at the end of the longest part,
    /// unless <see cref="Loop"/> is on, in which case it wraps around and always fills the buffer.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        var total = 0;
        while (true)
        {
            var read = ReadToEnd(buffer, offset + total, count - total);
            total += read;
            if (!Loop || total >= count - OutputChannels + 1)
                return total;
            if (read == 0)
            {
                if (PositionFrames < LengthFrames || LengthFrames == 0)
                    return total;   // nothing more to give (empty song): avoid spinning
                Seek(0);
            }
        }
    }

    private int ReadToEnd(float[] buffer, int offset, int count)
    {
        var frames = Math.Min(count / OutputChannels, (int)Math.Min(int.MaxValue, LengthFrames - PositionFrames));
        if (frames <= 0)
            return 0;

        var output = buffer.AsSpan(offset, frames * OutputChannels);
        output.Clear();

        foreach (var (kind, source) in _stems)
        {
            // Muted parts are still read so they stay in time when unmuted.
            var wanted = frames * source.Channels;
            if (_scratch.Length < wanted)
                _scratch = new float[wanted];
            var read = 0;
            while (read < wanted)
            {
                var n = source.Read(_scratch, read, wanted - read);
                if (n <= 0) break;
                read += n;
            }
            if (_muted.Contains(kind))
                continue;

            var readFrames = read / source.Channels;
            for (var f = 0; f < readFrames; f++)
            {
                float left, right;
                if (source.Channels == 1)
                    left = right = _scratch[f];
                else
                    (left, right) = (_scratch[f * 2], _scratch[f * 2 + 1]);
                output[f * 2] += left * PartGain;
                output[f * 2 + 1] += right * PartGain;
            }
        }

        for (var i = 0; i < output.Length; i++)
            output[i] = Math.Clamp(output[i], -1f, 1f);

        PositionFrames += frames;
        return frames * OutputChannels;
    }

    public void Dispose()
    {
        foreach (var (_, source) in _stems)
            source.Dispose();
    }
}
