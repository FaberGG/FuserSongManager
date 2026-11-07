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

/// <summary>A sine of one frequency, to see what a filter does to it.</summary>
internal sealed class SineSource(int sampleRate, long frames, double hz, float amplitude) : IStemSource
{
    public int SampleRate { get; } = sampleRate;
    public int Channels => 2;
    public long LengthFrames { get; } = frames;
    public long PositionFrames { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = (int)Math.Min(count / 2, LengthFrames - PositionFrames);
        for (var f = 0; f < frames; f++)
        {
            var v = (float)(amplitude * Math.Sin(2 * Math.PI * hz * PositionFrames / SampleRate));
            buffer[offset + f * 2] = v;
            buffer[offset + f * 2 + 1] = v;
            PositionFrames++;
        }
        return frames * 2;
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

    private static StemMixer TwoPartSong(long frames) =>
        new([
            (StemKind.Beat, new ConstantSource(Rate, frames, 1.0f)),
            (StemKind.Bass, new ConstantSource(Rate, frames, 0.5f)),
        ]) { Loop = true };

    [Fact]
    public void The_song_comes_back_with_every_part_on_even_those_muted_before_the_riser()
    {
        var song = TwoPartSong(10_000);
        song.SetMuted(StemKind.Bass, true);
        using var mixer = new TransitionMixer(song);
        mixer.StartRiser(RiserMixer(0.0f, 400), Plan(0, 0, 400, songAudible: false));

        var output = ReadFrames(mixer, 500);

        Assert.Equal(0.6f * 0.0f, At(output, 399), 4);                 // the riser (silent here) is all there is
        Assert.Equal(0.6f * 1.0f + 0.6f * 0.5f, At(output, 400), 4);   // the song, with the bass that was muted, from its first frame
        Assert.False(song.IsMuted(StemKind.Bass));
    }

    [Fact]
    public void Muted_parts_stay_muted_while_the_song_fades_under_the_riser()
    {
        var song = TwoPartSong(10_000);
        song.SetMuted(StemKind.Bass, true);
        using var mixer = new TransitionMixer(song);
        mixer.StartRiser(RiserMixer(0.0f, 400), Plan(0, 200, 400));

        var output = ReadFrames(mixer, 100);

        Assert.True(song.IsMuted(StemKind.Bass));
        Assert.True(At(output, 0) <= 0.6f + 1e-4f);   // only the beat: the muted bass is not added
    }

    [Fact]
    public void Skipping_a_sounding_riser_also_turns_every_part_on_and_cancelling_one_that_is_waiting_does_not()
    {
        var song = TwoPartSong(10_000);
        song.SetMuted(StemKind.Bass, true);
        using var mixer = new TransitionMixer(song);

        mixer.StartRiser(RiserMixer(0.0f, 400), Plan(100, 200, 400));
        mixer.Skip();   // still waiting for the bar line
        Assert.True(song.IsMuted(StemKind.Bass));

        mixer.StartRiser(RiserMixer(0.0f, 400), Plan(0, 200, 400));
        ReadFrames(mixer, 50);
        mixer.Skip();   // sounding
        Assert.False(song.IsMuted(StemKind.Bass));
    }

    // ---- the effects: what the song does while the riser comes in ----

    // At 1 kHz a bar of 8 000 frames is 8 s: long enough that the short stretches the effects work in do not show.
    private const int Bar = 8_000;

    // Frames for a number of bars.
    private static int B(double bars) => (int)(bars * Bar);

    private static RiserPlan EffectPlan(RiserEffect effect, long wait = 0, bool songAudible = true) =>
        new(wait, 0, 4L * Bar, Bar, songAudible, RiserEffects.Create(effect, Rate, Bar));

    // Four parts of known sizes: after the 0.6 gain they add up to 0.6 * 0.75 when all are on.
    private static StemMixer FourPartSong() => new(
    [
        (StemKind.Beat, new ConstantSource(Rate, 400_000, 0.4f)),
        (StemKind.Bass, new ConstantSource(Rate, 400_000, 0.2f)),
        (StemKind.Loop, new ConstantSource(Rate, 400_000, 0.1f)),
        (StemKind.Lead, new ConstantSource(Rate, 400_000, 0.05f)),
    ]) { Loop = true };

    private static float Expected(float beat, float bass, float loop, float lead) =>
        0.6f * (0.4f * beat + 0.2f * bass + 0.1f * loop + 0.05f * lead);

    [Fact]
    public void Strip_down_takes_the_parts_away_one_per_bar_and_the_beat_goes_last()
    {
        using var mixer = new TransitionMixer(FourPartSong());
        mixer.StartRiser(RiserMixer(0.0f, B(5)), EffectPlan(RiserEffect.StripDown));

        var output = ReadFrames(mixer, B(4) + 100);

        Assert.Equal(Expected(1, 1, 1, 1), At(output, 0), 3);             // all on as the riser starts
        Assert.Equal(Expected(1, 1, 1, 0), At(output, B(0.5)), 3);        // the lead goes first, in the opening part of bar 1
        Assert.Equal(Expected(1, 1, 1, 0), At(output, B(0.99)), 3);
        Assert.Equal(Expected(1, 1, 0, 0), At(output, B(1.5)), 3);        // the loop at the start of bar 2
        Assert.Equal(Expected(1, 0, 0, 0), At(output, B(2.5)), 3);        // the bass at the start of bar 3
        Assert.Equal(Expected(0.5f, 0, 0, 0), At(output, B(3.5)), 2);     // the beat fades through bar 4: halfway at 3.5
        Assert.Equal(Expected(1, 1, 1, 1), At(output, B(4) + 50), 3);     // the song is back, from its start, with every part
    }

    [Fact]
    public void Rhythm_hold_drops_lead_and_loop_in_the_first_bar_and_keeps_beat_and_bass_until_the_end()
    {
        using var mixer = new TransitionMixer(FourPartSong());
        mixer.StartRiser(RiserMixer(0.0f, B(5)), EffectPlan(RiserEffect.RhythmHold));

        var output = ReadFrames(mixer, B(4));

        Assert.Equal(Expected(1, 1, 0, 0), At(output, B(1.25)), 3);       // lead and loop already gone
        Assert.Equal(Expected(1, 1, 0, 0), At(output, B(2.4)), 3);        // the groove holds until the middle of bar 3
        Assert.Equal(0f, At(output, B(4) - 1), 2);                        // and it is gone as the riser ends
    }

    [Fact]
    public void Effects_leave_a_song_that_is_not_playing_alone_and_cancelling_gives_the_parts_back()
    {
        using (var mixer = new TransitionMixer(FourPartSong()))
        {
            mixer.StartRiser(RiserMixer(0.0f, B(5)), EffectPlan(RiserEffect.StripDown, songAudible: false));
            var output = ReadFrames(mixer, 500);
            Assert.All(Enumerable.Range(0, 500), f => Assert.Equal(0f, At(output, f), 4));   // only the riser (silent here)
        }

        using (var mixer = new TransitionMixer(FourPartSong()))
        {
            mixer.StartRiser(RiserMixer(0.0f, B(5)), EffectPlan(RiserEffect.StripDown));
            ReadFrames(mixer, B(2.8));   // lead, loop and bass already turned down
            mixer.Cancel();

            Assert.Equal(Expected(1, 1, 1, 1), At(ReadFrames(mixer, 1), 0), 3);
        }
    }

    [Fact]
    public void Trance_gate_chops_the_song_faster_each_bar()
    {
        using var mixer = new TransitionMixer(SongMixer(1.0f, 400_000));
        mixer.StartRiser(RiserMixer(0.0f, B(5)), EffectPlan(RiserEffect.TranceGate));

        var output = ReadFrames(mixer, B(4));

        // Bar 1: eighth notes (1 000 frames at this scale), open for 60 %.
        Assert.Equal(Song, At(output, 100), 3);
        Assert.Equal(0f, At(output, 800), 3);
        Assert.Equal(Song, At(output, 1_100), 3);
        // Bar 2: sixteenths (500 frames), open for half.
        Assert.True(At(output, B(1) + 100) > 0.3f);
        Assert.Equal(0f, At(output, B(1) + 400), 3);
        // Bar 4: the song is fading out as well.
        var loud = Enumerable.Range(B(3.9), 400).Max(f => At(output, f));
        Assert.True(loud < Song * 0.5f, $"still {loud}");
    }

    [Fact]
    public void Echo_out_cuts_the_song_and_its_last_beat_rings_on_getting_quieter()
    {
        const int wait = 6_000;   // 3 beats at this scale: the echo starts to catch the song for the last one
        using var mixer = new TransitionMixer(SongMixer(1.0f, 400_000));
        mixer.StartRiser(RiserMixer(0.0f, B(5)), EffectPlan(RiserEffect.EchoOut, wait: wait));

        var output = ReadFrames(mixer, wait + B(2));   // the wait, then half the riser

        Assert.Equal(Song, At(output, 0), 3);                          // the song plays untouched at first
        Assert.Equal(Song, At(output, 3_000), 3);                      // (the echo only adds to it in the last beat)
        var atCut = Enumerable.Range(wait, 400).Average(f => Math.Abs(At(output, f)));
        Assert.True(atCut > 0.05f, "the song was cut with nothing left to hear");
        Assert.True(atCut < Song * 0.9f, "the dry song is still there");
        var early = Enumerable.Range(wait, 3_000).Max(f => Math.Abs(At(output, f)));
        var late = Enumerable.Range(wait + 12_000, 3_000).Max(f => Math.Abs(At(output, f)));
        Assert.True(late < early * 0.1f, $"the echo does not die away: {early} then {late}");
    }

    [Fact]
    public void Dub_echo_rings_longer_and_darker_than_echo_out()
    {
        float Tail(RiserEffect effect)
        {
            using var mixer = new TransitionMixer(SongMixer(1.0f, 400_000));
            mixer.StartRiser(RiserMixer(0.0f, B(5)), EffectPlan(effect, wait: 6_000));
            var output = ReadFrames(mixer, 6_000 + B(2));
            return Enumerable.Range(6_000 + 9_000, 3_000).Max(f => Math.Abs(At(output, f)));
        }

        Assert.True(Tail(RiserEffect.DubEcho) > 3 * Tail(RiserEffect.EchoOut));
    }

    [Fact]
    public void Low_pass_filter_closes_over_two_bars_taking_the_highs_first_and_then_the_song_is_gone()
    {
        const int rate = 44_100;
        const long bar = 8_820;   // 0.2 s: short, so the test is quick

        float Level(double hz, long fromFrame, long toFrame)
        {
            using var song = new StemMixer([(StemKind.Beat, new SineSource(rate, 100_000, hz, 0.8f))]) { Loop = true };
            using var mixer = new TransitionMixer(song);
            mixer.StartRiser(
                new StemMixer([(StemKind.Beat, new ConstantSource(rate, 6 * bar, 0f))]),
                new RiserPlan(0, 0, 4 * bar, bar, true, RiserEffects.Create(RiserEffect.LowPassFilter, rate, bar)));
            var samples = new float[(int)(4 * bar) * 2];
            mixer.Read(samples, 0, samples.Length);
            var window = samples.Skip((int)fromFrame * 2).Take((int)(toFrame - fromFrame) * 2).Where((_, i) => i % 2 == 0);
            return (float)Math.Sqrt(window.Average(v => (double)v * v));
        }

        // A 6 kHz tone passes while the filter is open and is gone well before the end of the first bar.
        Assert.True(Level(6_000, 0, bar / 10) > 0.3f, "the highs were cut before the filter began to close");
        Assert.True(Level(6_000, bar, 3 * bar / 2) < 0.02f, "the highs are still there with the filter closed");
        // A 100 Hz tone gets through nearly to the end of the sweep...
        Assert.True(Level(100, bar, 3 * bar / 2) > 0.3f);
        // ... and the whole song is gone after bar 2.5 (the riser here is silent).
        Assert.True(Level(100, 5 * bar / 2, 4 * bar) < 1e-4f);
    }

    [Theory]
    [InlineData(RiserEffect.StripDown)]
    [InlineData(RiserEffect.RhythmHold)]
    [InlineData(RiserEffect.TranceGate)]
    [InlineData(RiserEffect.EchoOut)]
    public void Effects_sound_the_same_whatever_the_size_of_the_buffers_the_sound_card_asks_for(RiserEffect effect)
    {
        float[] Play(int chunk)
        {
            using var mixer = new TransitionMixer(FourPartSong());
            mixer.StartRiser(RiserMixer(0.1f, B(5)), EffectPlan(effect, wait: 1_500));
            return ReadFrames(mixer, 1_500 + B(4) + 500, chunk);
        }

        var whole = Play(int.MaxValue);
        foreach (var chunk in new[] { 1, 7, 333, 5_000 })
        {
            var other = Play(chunk);
            var worst = whole.Zip(other, (a, b) => Math.Abs(a - b)).Max();
            Assert.True(worst < 0.02f, $"{effect} with buffers of {chunk}: differs by {worst}");
        }
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
