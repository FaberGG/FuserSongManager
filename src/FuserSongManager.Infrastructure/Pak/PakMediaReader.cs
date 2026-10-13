using System.Text;
using CUE4Parse.UE4.Assets.Exports.Texture;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;
using SkiaSharp;

namespace FuserSongManager.Infrastructure.Pak;

/// <summary>
/// Album art and audio of a custom song pak (RF-PEN-13, RF-PEN-14). The layout follows FuserCustomSongCreator:
/// art in <c>UI/AlbumArt/T_{song}[_small]</c> (DXT1 textures); audio in the <c>patches/{song}{cel}_fusion</c>
/// asset of each part, one encrypted MOGG per keyzone.
/// </summary>
public sealed class PakMediaReader : ISongMediaReader
{
    private static readonly (StemKind Kind, string Suffix)[] Cels =
    [
        (StemKind.Beat, "bt"),
        (StemKind.Bass, "bs"),
        (StemKind.Loop, "lp"),
        (StemKind.Lead, "ld"),
    ];

    public byte[]? ReadAlbumArt(string pakPath)
    {
        using var provider = FuserPak.Open(pakPath);
        var song = FuserPak.ResolveSongName(provider, pakPath);

        // The 512x512 version is enough for the detail panel and four times cheaper to decode than the 1080x1080 one.
        var candidates = new[] { $"Fuser/Content/UI/AlbumArt/T_{song}_small.uasset", $"Fuser/Content/UI/AlbumArt/T_{song}.uasset" }
            .Concat(provider.Files.Keys.Where(k =>
                k.StartsWith("Fuser/Content/UI/AlbumArt/", StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)));
        var asset = candidates.FirstOrDefault(path => provider.TryGetGameFile(path, out _));
        if (asset is null)
            return null;

        var objectPath = asset[..^".uasset".Length] + "." + Path.GetFileNameWithoutExtension(asset);
        var texture = provider.LoadPackageObject<UTexture2D>(objectPath);
        var mip = texture.GetFirstMip();
        var data = mip?.BulkData?.Data ?? throw new InvalidDataException($"The album art {Path.GetFileName(asset)} has no image data.");
        int width = mip.SizeX, height = mip.SizeY;
        var rgba = texture.Format switch
        {
            EPixelFormat.PF_DXT1 => DxtDecoder.DecodeDxt1(data, width, height),
            EPixelFormat.PF_DXT5 => DxtDecoder.DecodeDxt5(data, width, height),
            _ => throw new NotSupportedException($"The album art uses the texture format {texture.Format}, which cannot be shown."),
        };
        return EncodePng(rgba, width, height);
    }

    private static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    public SongAudio ReadAudio(string pakPath, SongMode? mode)
    {
        using var provider = FuserPak.Open(pakPath);
        var song = FuserPak.ResolveSongName(provider, pakPath);
        var stems = new List<SongStem>(Cels.Length);
        var problems = new List<string>();

        foreach (var (kind, suffix) in Cels)
        {
            var path = $"Fuser/Content/Audio/Songs/{song}/{song}{suffix}/patches/{song}{suffix}_fusion.uexp";
            if (!provider.TryGetGameFile(path, out _))
            {
                // Paks whose folders do not follow the song name: any non-transition fusion asset of this part.
                path = provider.Files.Keys.FirstOrDefault(k =>
                    k.EndsWith($"{suffix}_fusion.uexp", StringComparison.OrdinalIgnoreCase) && !k.Contains("_trans_", StringComparison.OrdinalIgnoreCase)) ?? path;
            }
            if (!provider.TryGetGameFile(path, out _))
            {
                problems.Add($"{kind}: no audio asset");
                continue;
            }

            var files = HmxAudioPackage.Parse(provider.SaveAsset(path));
            var moggs = files.Where(f => f.FileType == HmxAudioPackage.MoggSampleResource).ToList();
            if (moggs.Count == 0)
            {
                problems.Add($"{kind}: no audio in its patch");
                continue;
            }

            var patch = files.FirstOrDefault(f => f.FileType == HmxAudioPackage.FusionPatchResource);
            var zones = patch is null ? [] : HmxAudioPackage.ParseKeyzones(Encoding.UTF8.GetString(patch.Data));
            foreach (var (label, mogg) in ChooseLayers(zones, moggs, mode))
            {
                var ogg = MoggCrypto.ToOgg(mogg.Data);
                // The creator allows empty parts (a song without bass): a valid Ogg with no samples.
                if (HasAudio(ogg))
                    stems.Add(new SongStem(kind, label, ogg));
            }
        }

        if (stems.Count == 0)
            throw new InvalidDataException(problems.Count == 0 ? "The song's parts are all empty." : $"The pak has no playable audio ({string.Join("; ", problems)}).");
        return new SongAudio(song, stems);
    }

    private static bool HasAudio(byte[] ogg)
    {
        using var vorbis = new NVorbis.VorbisReader(new MemoryStream(ogg, writable: false), closeOnDispose: true);
        return vorbis.TotalSamples > 0;
    }

    /// <summary>The MIDI note the game plays each part with: keyzones 0–71 hold the major version, 72–127 the minor one.</summary>
    private static int ModeNote(SongMode? mode) => mode == SongMode.Minor ? 84 : 60;

    /// <summary>
    /// The audio the game layers for the song's own key: every keyzone whose note range contains that key's note
    /// (zone labels are free text, the note range is what counts). Several zones can match, e.g. a sampler and a
    /// synth, or an unpitched vocal over 0–127; each distinct audio file is used once. Falls back to the first
    /// zone, then to the first audio, for patches that do not follow the convention.
    /// </summary>
    public static IReadOnlyList<(string Label, HmxPackageFile Mogg)> ChooseLayers(
        IReadOnlyList<FusionKeyzone> zones, IReadOnlyList<HmxPackageFile> moggs, SongMode? mode)
    {
        var note = ModeNote(mode);
        var chosen = zones.Where(z => z.MinNote <= note && note <= z.MaxNote).ToList();
        if (chosen.Count == 0 && zones.Count > 0)
            chosen.Add(zones[0]);

        var layers = new List<(string, HmxPackageFile)>();
        foreach (var zone in chosen)
        {
            var sample = Path.GetFileName(zone.SamplePath.Replace('\\', '/'));
            var mogg = moggs.FirstOrDefault(f => Path.GetFileName(f.FileName.Replace('\\', '/')).Equals(sample, StringComparison.OrdinalIgnoreCase))
                // Names did not resolve: the creator writes zones and MOGGs in the same order.
                ?? (zones.ToList().IndexOf(zone) is var index && index < moggs.Count ? moggs[index] : null);
            if (mogg is not null && layers.All(l => !ReferenceEquals(l.Item2, mogg)))
                layers.Add((zone.Label, mogg));
        }
        if (layers.Count == 0)
            layers.Add(("", moggs[0]));
        return layers;
    }
}
