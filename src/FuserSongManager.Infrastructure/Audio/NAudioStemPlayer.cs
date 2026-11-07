using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;
using NAudio.Wave;
using NVorbis;

namespace FuserSongManager.Infrastructure.Audio;

/// <summary>Ogg Vorbis part decoded on the fly with NVorbis (nothing is written to disk).</summary>
public sealed class VorbisStemSource(byte[] ogg) : IStemSource
{
    private readonly VorbisReader _reader = new(new MemoryStream(ogg, writable: false), closeOnDispose: true);

    public int SampleRate => _reader.SampleRate;
    public int Channels => _reader.Channels;
    public long LengthFrames => _reader.TotalSamples;

    public long PositionFrames
    {
        get => _reader.SamplePosition;
        set => Seek(Math.Clamp(value, 0, LengthFrames));
    }

    private void Seek(long frame)
    {
        try
        {
            _reader.SamplePosition = frame;
            return;
        }
        catch (InvalidDataException)
        {
            // NVorbis rejects the last page's granule position in the Oggs the custom song creator writes
            // ("GranulePos mismatch"), but decodes that page fine when reading through it. Land earlier and decode forward.
        }

        for (var back = (long)SampleRate * 2; ; back *= 2)
        {
            var start = Math.Max(0, frame - back);
            try
            {
                _reader.SamplePosition = start;
                break;
            }
            catch (InvalidDataException) when (start > 0)
            {
            }
        }

        var discard = new float[4096 * Channels];
        while (_reader.SamplePosition < frame)
        {
            var wanted = (int)Math.Min(discard.Length, (frame - _reader.SamplePosition) * Channels);
            if (_reader.ReadSamples(discard, 0, wanted) <= 0)
                break;
        }
    }

    public int Read(float[] buffer, int offset, int count) => _reader.ReadSamples(buffer, offset, count);

    public void Dispose() => _reader.Dispose();
}

/// <summary>Plays a song's parts through the default Windows audio device (WinMM via NAudio).</summary>
public sealed class NAudioStemPlayer : IStemPlayer
{
    /// <summary>A riser is four bars long: what its file has after that is a tail.</summary>
    private const int RiserBars = 4;

    private readonly Lock _gate = new();
    private StemMixer? _mixer;
    private TransitionMixer? _transition;   // the song's mixer with the riser on top; what the sound card reads
    private WaveOutEvent? _output;
    // Set when the mixer runs out of audio. WaveOut raises PlaybackStopped asynchronously for both the end of
    // the song and an explicit Stop, so this is what tells them apart.
    private volatile bool _reachedEnd;
    private bool _loop;

    public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;

    public void Load(SongAudio audio)
    {
        var sources = new List<(StemKind, IStemSource)>();
        try
        {
            foreach (var stem in audio.Stems)
                sources.Add((stem.Kind, new VorbisStemSource(stem.OggData)));
            var mixer = new StemMixer(sources) { Loop = _loop };
            ReleaseOutput();
            lock (_gate)
            {
                _transition?.Dispose();
                _mixer?.Dispose();
                _mixer = mixer;
                _transition = new TransitionMixer(mixer);
            }
        }
        catch
        {
            foreach (var (_, source) in sources)
                source.Dispose();
            throw;
        }
    }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Duration
    {
        get { lock (_gate) return _mixer is null ? TimeSpan.Zero : TimeSpan.FromSeconds((double)_mixer.LengthFrames / _mixer.SampleRate); }
    }

    public TimeSpan Position
    {
        get { lock (_gate) return _mixer is null ? TimeSpan.Zero : TimeSpan.FromSeconds((double)_mixer.PositionFrames / _mixer.SampleRate); }
        set
        {
            lock (_gate)
            {
                _transition?.Cancel();   // a riser would otherwise carry on over the new place
                _mixer?.Seek((long)(value.TotalSeconds * (_mixer?.SampleRate ?? 0)));
            }
        }
    }

    public void Play()
    {
        if (_mixer is null)
            throw new InvalidOperationException("Nothing is loaded.");
        if (_output is null)
        {
            _output = new WaveOutEvent { DesiredLatency = 150 };
            _output.Init(new MixerSampleProvider(this));
            _output.PlaybackStopped += OnPlaybackStopped;
        }
        lock (_gate)
        {
            if (_mixer.PositionFrames >= _mixer.LengthFrames)
                _mixer.Seek(0);
        }
        _reachedEnd = false;
        _output.Play();
    }

    public void Pause() => _output?.Pause();

    public bool IsRiserActive
    {
        get { lock (_gate) return _transition?.IsRiserActive ?? false; }
    }

    public void PlayRiser(SongAudio riser, TimeSpan? bar)
    {
        if (_mixer is null)
            throw new InvalidOperationException("Nothing is loaded.");
        var rate = _mixer.SampleRate;

        var sources = new List<(StemKind, IStemSource)>();
        StemMixer riserMixer;
        try
        {
            foreach (var stem in riser.Stems)
            {
                IStemSource source = new VorbisStemSource(stem.OggData);
                // A few songs have a riser at another rate than the song: everything shares the output's.
                if (source.SampleRate != rate)
                    source = new ResampledStemSource(source, rate);
                sources.Add((stem.Kind, source));
            }
            riserMixer = new StemMixer(sources);
        }
        catch
        {
            foreach (var (_, source) in sources)
                source.Dispose();
            throw;
        }

        var playing = IsPlaying;
        lock (_gate)
        {
            // Without a tempo there is no bar line: it starts at once, fades the song over half of it and the song
            // returns when the file ends.
            var plan = new RiserPlan(0, riserMixer.LengthFrames / 2, riserMixer.LengthFrames, 0, playing);
            if (bar is { } length && length > TimeSpan.Zero)
            {
                var barFrames = Math.Max(1, (long)Math.Round(length.TotalSeconds * rate));
                var wait = 0L;
                if (playing)
                {
                    // The riser comes in on the next bar line.
                    var position = _mixer.PositionFrames;
                    wait = Math.Min((position + barFrames - 1) / barFrames * barFrames, _mixer.LengthFrames) - position;
                }
                // The song fades over two bars and starts again after the riser's four. The riser file often goes on
                // past them with a silent or dying tail: it rings out for one more bar at most.
                plan = new RiserPlan(wait, 2 * barFrames, RiserBars * barFrames, barFrames, playing);
            }
            _transition!.StartRiser(riserMixer, plan);
        }

        if (!playing)
            Play();   // the output starts with the riser alone; the song follows it
    }

    public void StopRiser()
    {
        lock (_gate) _transition?.Skip();
    }

    public void Stop()
    {
        _output?.Stop();
        Position = TimeSpan.Zero;
    }

    public void SetMuted(StemKind stem, bool muted)
    {
        lock (_gate) _mixer?.SetMuted(stem, muted);
    }

    public bool IsMuted(StemKind stem)
    {
        lock (_gate) return _mixer?.IsMuted(stem) ?? false;
    }

    public bool Loop
    {
        get => _loop;
        set
        {
            lock (_gate)
            {
                _loop = value;
                _mixer?.Loop = value;
            }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null || _reachedEnd)
            PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(e.Exception));
    }

    private void ReleaseOutput()
    {
        if (_output is null)
            return;
        _output.PlaybackStopped -= OnPlaybackStopped;
        _output.Dispose();
        _output = null;
    }

    public void Dispose()
    {
        ReleaseOutput();
        lock (_gate)
        {
            _transition?.Dispose();
            _transition = null;
            _mixer?.Dispose();
            _mixer = null;
        }
    }

    /// <summary>The bridge NAudio pulls from on its playback thread.</summary>
    private sealed class MixerSampleProvider(NAudioStemPlayer player) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(player._mixer!.SampleRate, StemMixer.OutputChannels);

        public int Read(float[] buffer, int offset, int count)
        {
            int read;
            lock (player._gate)
                read = player._transition?.Read(buffer, offset, count) ?? 0;
            if (read == 0)
                player._reachedEnd = true;
            return read;
        }
    }
}
