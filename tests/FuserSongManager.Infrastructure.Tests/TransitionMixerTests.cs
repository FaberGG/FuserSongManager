using FuserSongManager.Core.Models;
using FuserSongManager.Infrastructure.Audio;

namespace FuserSongManager.Infrastructure.Tests;

/// <summary>A source that is the same value for its whole length (or a ramp), at any rate, so what a mixer does can be read off the samples.</summary>
internal sealed class ConstantSource(int sampleRate, long frames, float value, int channels = 2, bool ramp = false) : IStemSource
{
    public int SampleRate { get; } = sampleRate;
    public int Channels { get; } = channels;
    public long LengthFrames { get; } = frames;
    public long PositionFrames { get; set; }

    // A ramp's samples are their own frame number, so interpolation can be checked exactly.
    public int Read(float[] buffer, int offset, int count)
    {
        var frames = (int)Math.Min(count / Channels, LengthFrames - PositionFrames);
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < Channels; c++)
                buffer[offset + f * Channels + c] = ramp ? PositionFrames : value;
            PositionFrames++;
        }
        return frames * Channels;
    }

    public void Dispose()
    {
    }
}

public class TransitionMixerTests
{
    // 1 kHz so a frame is a millisecond. The mixer scales every part by 0.6.
    private const int Rate = 1000;
    private const float Song = 0.6f * 1.0f;
    private const float Riser = 0.6f * 0.5f;

    // The song loops, like the preview does.
    private static StemMixer SongMixer(float value, long frames) =>
        new([(StemKind.Beat, new ConstantSource(Rate, frames, value))]) { Loop = true };

    // A riser plays once.
    private static StemMixer RiserMixer(float value, long frames) =>
        new([(StemKind.Beat, new ConstantSource(Rate, frames, value))]);

    // The song returns when the riser's build-up ends; what is left of the riser file is a tail.
    private static RiserPlan Plan(long wait, long fade, long handOver, long tail = 0, bool songAudible = true) =>
        new(wait, fade, handOver, tail, songAudible);

    private static float[] ReadFrames(TransitionMixer mixer, int frames, int chunk = int.MaxValue)
    {
        var samples = new float[frames * 2];
        var done = 0;
        while (done < samples.Length)
        {
            var read = mixer.Read(samples, done, (int)Math.Min(samples.Length - done, (long)chunk * 2));
            Assert.True(read > 0, "the stream ended early");
            done += read;
        }
        return samples;
    }

    private static float At(float[] samples, int frame, int channel = 0) => samples[frame * 2 + channel];

    [Fact]
    public void The_song_plays_until_its_bar_ends_then_the_riser_comes_in_and_the_song_fades_under_it_and_returns_when_it_ends()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        // Riser: 400 frames; it starts after 100 frames of song; the song fades over 200 frames.
        mixer.StartRiser(RiserMixer(0.5f, 400), Plan(100, 200, 400, songAudible: true));
        Assert.Equal(RiserPhase.Waiting, mixer.Phase);

        var output = ReadFrames(mixer, 700);

        Assert.All(Enumerable.Range(0, 100), f => Assert.Equal(Song, At(output, f), 4));   // the song alone, to the end of the bar
        Assert.Equal(RiserPhase.None, mixer.Phase);
        Assert.Equal(Riser + Song, At(output, 100), 4);                                    // the riser starts with the song still at full volume
        Assert.Equal(Riser + Song * 0.5f, At(output, 200), 4);                             // halfway through the fade
        Assert.Equal(Riser, At(output, 300), 4);                                           // fade over: only the riser
        Assert.All(Enumerable.Range(300, 200), f => Assert.Equal(Riser, At(output, f), 4));
        Assert.All(Enumerable.Range(500, 200), f => Assert.Equal(Song, At(output, f), 4)); // the song returns on the very next frame
        Assert.Equal(200, mixer.Song.PositionFrames);                                      // ...from its beginning
    }

    [Fact]
    public void The_fade_only_goes_down_and_never_jumps_at_either_end()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.StartRiser(RiserMixer(0.0f, 400), Plan(0, 200, 400, songAudible: true));

        var output = ReadFrames(mixer, 400);

        Assert.Equal(Song, At(output, 0), 4);
        Assert.Equal(0f, At(output, 200), 4);
        for (var f = 1; f < 400; f++)
        {
            Assert.True(At(output, f) <= At(output, f - 1) + 1e-6, $"frame {f} went up");
            Assert.True(Math.Abs(At(output, f) - At(output, f - 1)) < 0.01f, $"frame {f} jumped");
        }
    }

    [Fact]
    public void The_handover_does_not_depend_on_how_the_stream_is_cut_into_buffers()
    {
        float[] Play(int chunk)
        {
            using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
            mixer.StartRiser(RiserMixer(0.5f, 400), Plan(100, 200, 400, songAudible: true));
            return ReadFrames(mixer, 700, chunk);
        }

        var whole = Play(int.MaxValue);
        Assert.Equal(whole, Play(7));      // buffers that end in the middle of every phase
        Assert.Equal(whole, Play(1));
        Assert.Equal(whole, Play(333));
    }

    [Fact]
    public void Without_the_song_playing_the_riser_is_heard_alone_and_the_song_follows_from_its_beginning()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.Song.Seek(5_000);   // the song was paused somewhere in the middle
        mixer.StartRiser(RiserMixer(0.5f, 400), Plan(0, 200, 400, songAudible: false));

        var output = ReadFrames(mixer, 500);

        Assert.All(Enumerable.Range(0, 400), f => Assert.Equal(Riser, At(output, f), 4));
        Assert.All(Enumerable.Range(400, 100), f => Assert.Equal(Song, At(output, f), 4));
        Assert.Equal(100, mixer.Song.PositionFrames);   // not where it was paused
    }

    [Fact]
    public void Cancelling_leaves_the_song_at_full_volume_where_it_was()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.StartRiser(RiserMixer(0.5f, 400), Plan(100, 200, 400, songAudible: true));
        ReadFrames(mixer, 150);   // into the fade

        mixer.Cancel();

        Assert.False(mixer.IsRiserActive);
        var output = ReadFrames(mixer, 10);
        Assert.All(Enumerable.Range(0, 10), f => Assert.Equal(Song, At(output, f), 4));
        Assert.Equal(160, mixer.Song.PositionFrames);   // it did not restart
    }

    [Fact]
    public void Skipping_before_the_riser_sounds_cancels_it_and_after_it_starts_the_song_over()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.StartRiser(RiserMixer(0.5f, 400), Plan(100, 200, 400, songAudible: true));
        ReadFrames(mixer, 50);
        mixer.Skip();   // still waiting for the bar line
        Assert.False(mixer.IsRiserActive);
        Assert.Equal(50, mixer.Song.PositionFrames);

        mixer.StartRiser(RiserMixer(0.5f, 400), Plan(0, 200, 400, songAudible: true));
        ReadFrames(mixer, 120);
        mixer.Skip();   // the riser is sounding

        Assert.False(mixer.IsRiserActive);
        Assert.Equal(0, mixer.Song.PositionFrames);
        Assert.Equal(Song, At(ReadFrames(mixer, 1), 0), 4);
    }

    [Fact]
    public void The_fade_cannot_outlast_the_riser()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.StartRiser(RiserMixer(0.0f, 400), Plan(0, 5_000, 400));

        var output = ReadFrames(mixer, 400);

        Assert.True(Math.Abs(At(output, 399)) < 0.001f, "the song was not faded out when the riser ended");
    }

    [Fact]
    public void A_riser_asked_while_one_is_active_replaces_it()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.StartRiser(RiserMixer(0.5f, 400), Plan(0, 200, 400, songAudible: true));
        ReadFrames(mixer, 100);

        mixer.StartRiser(RiserMixer(0.25f, 300), Plan(50, 100, 300, songAudible: true));

        Assert.Equal(RiserPhase.Waiting, mixer.Phase);
    }

    [Fact]
    public void The_song_returns_when_the_risers_bars_end_and_the_tail_of_the_file_rings_out_under_it()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        // A riser file of 700 frames whose build-up is the first 400: 300 frames of tail, allowed to ring 100.
        mixer.StartRiser(RiserMixer(0.5f, 700), Plan(0, 200, 400, tail: 100, songAudible: true));

        var output = ReadFrames(mixer, 800);

        Assert.Equal(Riser, At(output, 300), 4);                       // build-up, song already faded away
        Assert.Equal(Riser, At(output, 399), 4);
        Assert.Equal(Song + Riser, At(output, 400), 4);                // the song is back on the very frame the bars end, from its start
        Assert.Equal(Song + Riser * 0.5f, At(output, 450), 4);         // the tail fades over one bar
        Assert.Equal(Song, At(output, 500), 4);                        // gone: the file's remaining 200 frames are not heard
        Assert.All(Enumerable.Range(500, 300), f => Assert.Equal(Song, At(output, f), 4));
        Assert.Equal(400, mixer.Song.PositionFrames);                  // 800 - 400: it restarted at 400, not later
        Assert.Equal(RiserPhase.None, mixer.Phase);
    }

    [Fact]
    public void The_riser_counts_as_over_for_the_app_once_the_song_is_back_even_while_its_tail_rings()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.StartRiser(RiserMixer(0.5f, 700), Plan(0, 200, 400, tail: 100));

        ReadFrames(mixer, 399);
        Assert.True(mixer.IsRiserActive);
        ReadFrames(mixer, 2);
        Assert.Equal(RiserPhase.Tail, mixer.Phase);
        Assert.False(mixer.IsRiserActive);
    }

    [Fact]
    public void A_riser_file_far_longer_than_its_bars_does_not_hang_over_the_song()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.StartRiser(RiserMixer(0.5f, 5_000), Plan(0, 200, 400, tail: 100));

        var output = ReadFrames(mixer, 1_000);

        Assert.All(Enumerable.Range(520, 400), f => Assert.Equal(Song, At(output, f), 4));
    }

    [Fact]
    public void Skipping_during_the_tail_only_drops_what_is_left_of_the_riser()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        mixer.StartRiser(RiserMixer(0.5f, 700), Plan(0, 200, 400, tail: 100));
        ReadFrames(mixer, 420);

        mixer.Skip();

        Assert.Equal(20, mixer.Song.PositionFrames);   // the song was not restarted again
        Assert.Equal(Song, At(ReadFrames(mixer, 1), 0), 4);
    }

    [Fact]
    public void Parts_of_a_riser_may_be_stereo_or_mono_and_shorter_than_the_longest()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 10_000));
        var riser = new StemMixer(
        [
            (StemKind.Beat, new ConstantSource(Rate, 400, 0.5f)),
            (StemKind.Bass, new ConstantSource(Rate, 250, 0.5f, channels: 1)),
        ]);
        mixer.StartRiser(riser, Plan(0, 0, 400, songAudible: false));

        var output = ReadFrames(mixer, 450);

        Assert.Equal(Riser * 2, At(output, 100), 4);    // both parts
        Assert.Equal(Riser, At(output, 300), 4);        // the shorter one has ended
        Assert.Equal(Song, At(output, 400), 4);
    }
}

public class ResampledStemSourceTests
{
    [Fact]
    public void Doubling_the_rate_interpolates_between_frames()
    {
        using var source = new ResampledStemSource(new ConstantSource(1000, 100, 0, channels: 1, ramp: true), 2000);

        Assert.Equal((2000, 200), (source.SampleRate, (int)source.LengthFrames));
        var buffer = new float[200];
        Assert.Equal(200, source.Read(buffer, 0, buffer.Length));

        Assert.Equal(0f, buffer[0], 4);
        Assert.Equal(0.5f, buffer[1], 4);
        Assert.Equal(1f, buffer[2], 4);
        Assert.Equal(50f, buffer[100], 4);
        Assert.Equal(0, source.Read(buffer, 0, buffer.Length));   // the end
    }

    [Fact]
    public void Lowering_the_rate_skips_frames_and_keeps_both_channels()
    {
        using var source = new ResampledStemSource(new ConstantSource(48_000, 4_800, 0, channels: 2, ramp: true), 16_000);

        Assert.Equal(1_600, source.LengthFrames);
        var buffer = new float[2 * 10];
        source.Read(buffer, 0, buffer.Length);

        for (var f = 0; f < 10; f++)
            Assert.Equal((f * 3f, f * 3f), (buffer[f * 2], buffer[f * 2 + 1]));
    }

    [Fact]
    public void Reading_in_pieces_or_after_seeking_gives_the_same_samples()
    {
        float[] ReadAll(Action<ResampledStemSource>? prepare = null)
        {
            using var source = new ResampledStemSource(new ConstantSource(44_100, 4_410, 0, channels: 1, ramp: true), 48_000);
            prepare?.Invoke(source);
            var all = new float[(int)source.LengthFrames - (int)source.PositionFrames];
            var done = 0;
            while (done < all.Length)
            {
                var read = source.Read(all, done, Math.Min(37, all.Length - done));
                if (read == 0) break;
                done += read;
            }
            return all;
        }

        var whole = ReadAll();
        Assert.Equal(whole[500..], ReadAll(s => s.PositionFrames = 500));
        Assert.All(whole.Zip(whole.Skip(1)), pair => Assert.True(pair.Second >= pair.First));   // a ramp stays a ramp
    }
}
