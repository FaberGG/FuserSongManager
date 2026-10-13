using System.Buffers.Binary;
using System.Text;
using FuserSongManager.Core.Models;
using FuserSongManager.Infrastructure.Audio;
using FuserSongManager.Infrastructure.Pak;
using NVorbis;
using SkiaSharp;

namespace FuserSongManager.Infrastructure.Tests;

public class MoggCryptoTests
{
    private static readonly byte[] Ogg = Encoding.ASCII.GetBytes("OggS" + new string('x', 5000) + "end");

    /// <summary>A MOGG as moggcrypt writes it: version, Ogg offset, a map, the 16-byte counter, the data.</summary>
    internal static byte[] MakeMogg(byte[] ogg, int version, byte[]? counter = null)
    {
        counter ??= [0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        var map = new byte[12];   // empty OggMap header
        var header = new byte[8 + map.Length + 16];
        BinaryPrimitives.WriteInt32LittleEndian(header, version);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), header.Length);
        counter.CopyTo(header, header.Length - 16);
        var body = version == MoggCrypto.AesCtr ? MoggCrypto.Transform(ogg, counter) : ogg;
        return [.. header, .. body];
    }

    [Fact]
    public void Encrypted_mogg_becomes_the_original_ogg()
    {
        var mogg = MakeMogg(Ogg, MoggCrypto.AesCtr);

        Assert.NotEqual(Ogg, mogg[^Ogg.Length..]);   // really encrypted
        Assert.Equal(Ogg, MoggCrypto.ToOgg(mogg));
    }

    [Fact]
    public void Counter_increments_only_its_low_64_bits_like_moggcrypt()
    {
        // Low qword at its maximum: moggcrypt wraps it to 0 without carrying into the high half.
        byte[] counter = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3, 4, 5, 6, 7, 8];
        var twoBlocks = new byte[32];

        var keystream = MoggCrypto.Transform(twoBlocks, counter);
        byte[] secondCounter = [0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8];

        Assert.Equal(MoggCrypto.Transform(new byte[16], secondCounter), keystream[16..]);
    }

    [Fact]
    public void Unencrypted_mogg_is_returned_as_is()
    {
        Assert.Equal(Ogg, MoggCrypto.ToOgg(MakeMogg(Ogg, MoggCrypto.Unencrypted)));
    }

    [Fact]
    public void Other_versions_are_refused_with_a_clear_message()
    {
        var ex = Assert.Throws<NotSupportedException>(() => MoggCrypto.ToOgg(MakeMogg(Ogg, 0x10)));
        Assert.Contains("version 0x10", ex.Message);
        Assert.Throws<InvalidDataException>(() => MoggCrypto.ToOgg(new byte[4]));
    }
}

public class HmxAudioPackageTests
{
    private static void WriteFString(List<byte> bytes, string value)
    {
        bytes.AddRange(BitConverter.GetBytes(value.Length + 1));
        bytes.AddRange(Encoding.ASCII.GetBytes(value));
        bytes.Add(0);
    }

    /// <summary>The package layout of FuserCustomSongCreator's HmxAudio::PackageFile, after some unrelated bytes.</summary>
    internal static byte[] MakePackage(string patchText, params (string Name, byte[] Mogg)[] moggs)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("unrelated UE header bytes"));
        bytes.AddRange(BitConverter.GetBytes((long)(moggs.Length + 1)));

        void File(string name, string type, byte[] body)
        {
            bytes.AddRange(BitConverter.GetBytes(0));
            WriteFString(bytes, name);
            bytes.AddRange(BitConverter.GetBytes(0u));
            WriteFString(bytes, type);
            bytes.AddRange(BitConverter.GetBytes((long)body.Length));
            bytes.AddRange(body);
        }

        File("fusion.fusion", HmxAudioPackage.FusionPatchResource, Encoding.UTF8.GetBytes(patchText));
        foreach (var (name, mogg) in moggs)
        {
            var header = new byte[32];
            Encoding.ASCII.GetBytes("mogs").CopyTo(header, 0);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), 44100);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), mogg.Length);
            File(name, HmxAudioPackage.MoggSampleResource, [.. header, .. mogg]);
        }
        bytes.AddRange(Encoding.ASCII.GetBytes("trailing export bytes"));
        return [.. bytes];
    }

    internal const string Patch = """
        (keymap
           (keyzone
              (keymap_preset 0)
              (zone_label
                 "Major"
              )
              (sample_path
                 "C:/Song_ld_0.mogg"
              )
              (root_note 60)
              (min_note 0)
              (max_note 71)
              (unpitched 0)
              (timestretch_settings (maintain_time 1))
           )
           (keyzone
              (zone_label "Minor")
              (sample_path "C:/Song_ld_1.mogg")
              (min_note 72) (max_note 127)
              (unpitched 0)
           )
           (keyzone
              (zone_label "Unpitched (weird)")
              (sample_path "C:/Song_ld_2.mogg")
              (min_note 0) (max_note 127)
              (unpitched 1)
           )
        )
        """;

    [Fact]
    public void Resources_are_found_with_their_names_and_data()
    {
        var package = MakePackage(Patch, ("C:/Song_ld_0.mogg", [1, 2, 3]), ("C:/Song_ld_1.mogg", [4, 5]));

        var files = HmxAudioPackage.Parse(package);

        Assert.Equal(["fusion.fusion", "C:/Song_ld_0.mogg", "C:/Song_ld_1.mogg"], files.Select(f => f.FileName));
        Assert.Equal([4, 5], files[2].Data);
        Assert.Contains("keyzone", Encoding.UTF8.GetString(files[0].Data));
    }

    [Fact]
    public void Truncated_package_is_reported()
    {
        var package = MakePackage(Patch, ("C:/a.mogg", new byte[100]));

        Assert.Throws<InvalidDataException>(() => HmxAudioPackage.Parse(package.AsSpan(0, package.Length - 60)));
    }

    [Fact]
    public void Keyzones_are_read_with_labels_paths_and_pitch()
    {
        var zones = HmxAudioPackage.ParseKeyzones(Patch);

        Assert.Equal(
            [new FusionKeyzone("Major", "C:/Song_ld_0.mogg", false, 0, 71), new FusionKeyzone("Minor", "C:/Song_ld_1.mogg", false, 72, 127), new FusionKeyzone("Unpitched (weird)", "C:/Song_ld_2.mogg", true, 0, 127)],
            zones);
    }

    private static readonly List<HmxPackageFile> ThreeMoggs =
        Enumerable.Range(0, 3).Select(i => new HmxPackageFile($"C:/Song_ld_{i}.mogg", HmxAudioPackage.MoggSampleResource, [(byte)i])).ToList();

    [Theory]
    [InlineData(SongMode.Major, new byte[] { 0, 2 })]
    [InlineData(SongMode.Minor, new byte[] { 1, 2 })]
    [InlineData(null, new byte[] { 0, 2 })]
    public void Every_zone_covering_the_songs_key_is_layered(SongMode? mode, byte[] expected)
    {
        // The unpitched zone spans 0-127, so it plays in both keys, on top of the pitched one.
        var layers = PakMediaReader.ChooseLayers(HmxAudioPackage.ParseKeyzones(Patch), ThreeMoggs, mode);

        Assert.Equal(expected, layers.Select(l => l.Mogg.Data[0]));
    }

    [Fact]
    public void Zones_without_labels_sharing_one_audio_use_it_once()
    {
        // Older creator versions: no zone_label, the same sample for both keys.
        var zones = new[] { new FusionKeyzone("", "C:/Song_ld_0.mogg", false, 0, 71), new FusionKeyzone("", "C:/Song_ld_0.mogg", false, 72, 127) };

        var layers = PakMediaReader.ChooseLayers(zones, ThreeMoggs, SongMode.Minor);

        Assert.Equal(0, Assert.Single(layers).Mogg.Data[0]);
    }

    [Fact]
    public void Patches_outside_the_convention_fall_back_to_the_first_zone_or_audio()
    {
        // A drum kit: one-shot zones on single low notes, none covering the key's note.
        var drums = new[] { new FusionKeyzone("00_Kick", "C:/Song_ld_1.mogg", true, 0, 0), new FusionKeyzone("01_Snare", "C:/Song_ld_2.mogg", true, 2, 2) };

        Assert.Equal(1, Assert.Single(PakMediaReader.ChooseLayers(drums, ThreeMoggs, SongMode.Major)).Mogg.Data[0]);
        Assert.Equal(0, Assert.Single(PakMediaReader.ChooseLayers([], ThreeMoggs, SongMode.Minor)).Mogg.Data[0]);
    }
}

public class StemMixerTests
{
    private sealed class ConstantSource(float value, long frames, int channels = 2, int sampleRate = 44100) : IStemSource
    {
        public int SampleRate => sampleRate;
        public int Channels => channels;
        public long LengthFrames => frames;
        public long PositionFrames { get; set; }
        public bool Disposed { get; private set; }

        public int Read(float[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count / channels, frames - PositionFrames);
            for (var i = 0; i < n * channels; i++)
                buffer[offset + i] = value;
            PositionFrames += n;
            return n * channels;
        }

        public void Dispose() => Disposed = true;
    }

    private static float[] Read(StemMixer mixer, int frames)
    {
        var buffer = new float[frames * 2];
        var read = mixer.Read(buffer, 0, buffer.Length);
        return buffer[..read];
    }

    [Fact]
    public void Parts_are_summed_and_muted_parts_stay_in_time()
    {
        var bass = new ConstantSource(0.5f, 100);
        using var mixer = new StemMixer([(StemKind.Beat, new ConstantSource(0.25f, 100)), (StemKind.Bass, bass)]);

        Assert.Equal(0.75f * 0.6f, Read(mixer, 10)[0], 4);

        mixer.SetMuted(StemKind.Bass, true);
        Assert.Equal(0.25f * 0.6f, Read(mixer, 10)[0], 4);
        Assert.Equal(20, bass.PositionFrames);   // still read while muted
        Assert.Equal(20, mixer.PositionFrames);
    }

    [Fact]
    public void Mono_parts_are_spread_to_both_sides_and_loud_mixes_are_clamped()
    {
        using var mixer = new StemMixer([(StemKind.Lead, new ConstantSource(1f, 50, channels: 1)), (StemKind.Loop, new ConstantSource(1f, 50))]);

        var samples = Read(mixer, 4);

        Assert.Equal(8, samples.Length);
        Assert.All(samples, s => Assert.Equal(1f, s));
    }

    [Fact]
    public void Reading_ends_with_the_longest_part_and_seeking_moves_every_part()
    {
        var shortPart = new ConstantSource(0.5f, 10);
        using var mixer = new StemMixer([(StemKind.Beat, shortPart), (StemKind.Bass, new ConstantSource(0.5f, 30))]);

        Assert.Equal(30, Read(mixer, 100).Length / 2);
        Assert.Empty(Read(mixer, 10));

        mixer.Seek(5);
        Assert.Equal((5, 5L), (mixer.PositionFrames, shortPart.PositionFrames));
    }

    [Fact]
    public void Parts_with_different_sample_rates_are_refused()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            new StemMixer([(StemKind.Beat, new ConstantSource(0, 1)), (StemKind.Lead, new ConstantSource(0, 1, sampleRate: 48000))]));
        Assert.Contains("Lead", ex.Message);
    }
}

/// <summary>Reads real paks: album art and audio of every sample song.</summary>
public class DxtDecoderTests
{
    [Fact]
    public void Dxt1_block_expands_to_its_palette()
    {
        // c0 = pure red, c1 = pure blue; indices: pixel 0 -> c0, pixel 1 -> c1, pixel 2 -> 2/3 c0 + 1/3 c1, rest c0.
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0b_10_01_00 & 0xFF, 0, 0, 0];

        var rgba = DxtDecoder.DecodeDxt1(block, 4, 4);

        Assert.Equal([255, 0, 0, 255], rgba[..4]);
        Assert.Equal([0, 0, 255, 255], rgba[4..8]);
        Assert.Equal([170, 0, 85, 255], rgba[8..12]);
    }

    [Fact]
    public void Images_not_multiple_of_four_are_cropped_and_short_data_is_refused()
    {
        Assert.Equal(2 * 3 * 4, DxtDecoder.DecodeDxt1(new byte[8], 2, 3).Length);
        Assert.Throws<InvalidDataException>(() => DxtDecoder.DecodeDxt1(new byte[8], 8, 8));
    }

    [Fact]
    public void Dxt5_reads_interpolated_alpha()
    {
        // Alpha endpoints 255 and 0, every pixel index 1 (= a1 = 0); colour block all c0 = white.
        byte[] block = [255, 0, 0b_001_001_001 & 0xFF, 0b_001_001_001 >> 8 | 0b_0100_1001 & 0xFF, 0x92, 0x24, 0x49, 0x92, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0];

        var rgba = DxtDecoder.DecodeDxt5(block, 4, 4);

        Assert.Equal([255, 255, 255, 0], rgba[..4]);
    }
}

public class PakMediaReaderTests
{
    private static readonly PakMediaReader Reader = new();

    // The two sample paks made without album art.
    private static readonly string[] WithoutArt = ["TOP_Heat_ShpQn", "We_Found_Love"];

    public static IEnumerable<string> SamplePaks() =>
        SamplePaksFactAttribute.Directory is { } dir ? Directory.GetFiles(dir, "*.pak", SearchOption.AllDirectories) : [];

    [SamplePaksFact]
    public void Album_art_is_decoded_as_a_512_png_or_reported_missing()
    {
        foreach (var pak in SamplePaks())
        {
            var png = Reader.ReadAlbumArt(pak);
            if (WithoutArt.Any(name => Path.GetFileName(pak).StartsWith(name, StringComparison.OrdinalIgnoreCase)))
            {
                Assert.Null(png);
                continue;
            }
            Assert.NotNull(png);
            using var bitmap = SKBitmap.Decode(png);
            Assert.Equal((512, 512), (bitmap.Width, bitmap.Height));
        }
    }

    [SamplePaksFact]
    public void Every_song_has_its_playable_parts()
    {
        var metadata = new CueSongMetadataReader(PatchedDlcCatalog.LoadEmbedded());
        foreach (var pak in SamplePaks())
        {
            var mode = metadata.Read(pak).Metadata.Mode;

            var audio = Reader.ReadAudio(pak, mode);

            // Parts come in mixing order; a part can be missing when the author left it empty
            // (TheyDontCareAboutUsMJ has no bass), but every song has most of them.
            var kinds = audio.Stems.Select(s => s.Kind).Distinct().ToList();
            Assert.Equal(kinds.Order(), kinds);
            Assert.True(kinds.Count >= 3, $"{Path.GetFileName(pak)}: {string.Join(", ", kinds)}");
            foreach (var stem in audio.Stems)
            {
                Assert.Equal("OggS", Encoding.ASCII.GetString(stem.OggData, 0, 4));
                using var vorbis = new VorbisReader(new MemoryStream(stem.OggData), true);
                Assert.True(vorbis.TotalTime > TimeSpan.FromSeconds(10), $"{Path.GetFileName(pak)} {stem.Kind}: {vorbis.TotalTime}");
            }
        }
    }

    [SamplePaksFact]
    public void Decoded_parts_mix_into_audible_sound()
    {
        var pak = SamplePaks().First();
        var audio = Reader.ReadAudio(pak, null);
        using var mixer = new StemMixer(audio.Stems.Select(s => (s.Kind, (IStemSource)new VorbisStemSource(s.OggData))).ToList());

        mixer.Seek(mixer.SampleRate * 20L);   // well into the song
        var buffer = new float[mixer.SampleRate * 2];
        var read = mixer.Read(buffer, 0, buffer.Length);
        var rms = Math.Sqrt(buffer.Take(read).Select(s => (double)s * s).Average());

        Assert.Equal(buffer.Length, read);
        Assert.True(rms > 0.01, $"rms {rms}");
    }

    [SamplePaksFact]
    public void Seeking_works_anywhere_including_the_last_page()
    {
        // NVorbis alone throws "GranulePos mismatch" when seeking into the last page of these Oggs.
        foreach (var pak in SamplePaks().Take(6))
        foreach (var stem in Reader.ReadAudio(pak, null).Stems)
        {
            using var source = new VorbisStemSource(stem.OggData);
            foreach (var fraction in new[] { 0.5, 0.97, 0.995, 0.2 })
            {
                var target = (long)(source.LengthFrames * fraction);
                source.PositionFrames = target;
                Assert.Equal(target, source.PositionFrames);
            }

            source.PositionFrames = source.LengthFrames - 1000;
            var buffer = new float[8192];
            long frames = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                frames += read / source.Channels;
            Assert.Equal(1000, frames);
        }
    }

    [Fact]
    public void A_file_that_is_not_a_pak_is_reported()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a pak");
            Assert.Throws<InvalidDataException>(() => Reader.ReadAudio(path, null));
            Assert.Throws<InvalidDataException>(() => Reader.ReadAlbumArt(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
