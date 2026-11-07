namespace FuserSongManager.Infrastructure.Audio;

/// <summary>
/// Plays another part at a different sample rate by linear interpolation. Used for the few custom songs whose riser
/// was exported at another rate than the song (44.1, 48 and even 16 kHz): everything that goes through one output
/// must share its rate. Linear interpolation is not hi-fi, but it is more than enough for a short effect.
/// </summary>
public sealed class ResampledStemSource : IStemSource
{
    private readonly IStemSource _inner;
    private readonly double _step;   // input frames per output frame
    private readonly float[] _a;     // the input frame at _index
    private readonly float[] _b;     // the one after it
    private long _index = -1;        // input frame held in _a; -1 until the first read
    private long _position;          // output frames delivered

    public ResampledStemSource(IStemSource inner, int sampleRate)
    {
        _inner = inner;
        SampleRate = sampleRate;
        _step = (double)inner.SampleRate / sampleRate;
        _a = new float[inner.Channels];
        _b = new float[inner.Channels];
        LengthFrames = (long)Math.Round(inner.LengthFrames / _step);
    }

    public int SampleRate { get; }
    public int Channels => _inner.Channels;
    public long LengthFrames { get; }

    public long PositionFrames
    {
        get => _position;
        set
        {
            _position = Math.Clamp(value, 0, LengthFrames);
            _index = -1;   // the next read looks the input up again
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = Channels;
        var frames = (int)Math.Min(count / channels, LengthFrames - _position);
        for (var f = 0; f < frames; f++)
        {
            var source = _position * _step;
            var whole = (long)source;
            var fraction = (float)(source - whole);
            MoveTo(whole);
            for (var c = 0; c < channels; c++)
                buffer[offset + f * channels + c] = _a[c] + (_b[c] - _a[c]) * fraction;
            _position++;
        }
        return frames * channels;
    }

    /// <summary>Makes <c>_a</c> the input frame <paramref name="target"/> and <c>_b</c> the next, reading ahead only as needed.</summary>
    private void MoveTo(long target)
    {
        if (_index < 0 || target < _index || target > _index + 1_000_000)
        {
            _inner.PositionFrames = target;
            ReadFrame(_a);
            ReadFrame(_b);
            _index = target;
            return;
        }
        while (_index < target)
        {
            Array.Copy(_b, _a, _a.Length);
            ReadFrame(_b);
            _index++;
        }
    }

    // At the end of the input the last frame is held, so the interpolation never reads past it.
    private void ReadFrame(float[] frame)
    {
        if (_inner.Read(frame, 0, frame.Length) < frame.Length)
            Array.Copy(_a, frame, frame.Length);
    }

    public void Dispose() => _inner.Dispose();
}
