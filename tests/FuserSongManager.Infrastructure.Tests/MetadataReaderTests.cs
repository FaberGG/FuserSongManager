using System.Text.Json;
using FuserSongManager.Core.Models;
using FuserSongManager.Infrastructure.Pak;

namespace FuserSongManager.Infrastructure.Tests;

public sealed class MetadataReaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CueSongMetadataReader _reader = new(PatchedDlcCatalog.LoadEmbedded());

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Patched_dlc_catalog_has_the_133_v1_songs()
    {
        var catalog = PatchedDlcCatalog.LoadEmbedded();

        Assert.Equal(133, catalog.Count);
        Assert.True(catalog.TryGet("AdoreYou", out var adoreYou));
        Assert.Equal(new SongMetadata("Adore You", "Harry Styles", 2019, Genre.Pop, MusicalKey.Eb, SongMode.Major, 99), adoreYou);
    }

    [Fact]
    public void Patched_dlc_songs_come_from_the_catalog_without_opening_the_pak()
    {
        // The file does not even exist: the catalog is used by name, as in v1.
        var result = _reader.Read(_temp.File("adoreyou_P.pak"));

        Assert.Equal(ReadStatus.Ok, result.Status);
        Assert.Equal(MetadataSource.PatchedDlcCatalog, result.Source);
        Assert.Equal("Harry Styles", result.Metadata.Artist);
    }

    [Fact]
    public void A_file_that_is_not_a_pak_fails_with_a_clear_reason()
    {
        var path = _temp.File("Broken_P.pak");
        File.WriteAllBytes(path, Enumerable.Range(0, 5000).Select(i => (byte)(i * 31)).ToArray());

        var result = _reader.Read(path);

        Assert.Equal(ReadStatus.Failed, result.Status);
        Assert.Equal(SongMetadata.Empty, result.Metadata);
        Assert.Contains("not a valid or readable pak", result.Error);
    }

    [SamplePaksFact]
    public void Sample_paks_match_the_v1_parser()
    {
        var expected = JsonSerializer.Deserialize<List<V1Reference>>(File.ReadAllText(Path.Combine("TestData", "v1_reference.json")))!
            .ToDictionary(r => r.short_name, StringComparer.OrdinalIgnoreCase);
        var paks = Directory.EnumerateFiles(SamplePaksFactAttribute.Directory!, "*.pak", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(paks);

        var mismatches = new List<string>();
        var compared = 0;
        foreach (var pak in paks)
        {
            var name = FuserPak.ShortName(pak);
            if (!expected.TryGetValue(name, out var v1))
                continue;
            compared++;
            var result = _reader.Read(pak);
            var v1Metadata = new SongMetadata(v1.title, v1.artist, v1.year, Enum.Parse<Genre>(v1.genre),
                Enum.TryParse<MusicalKey>(v1.key, out var key) ? key : null,
                Enum.TryParse<SongMode>(v1.mode, out var mode) ? mode : null, v1.bpm);
            if (result.Status != ReadStatus.Ok || result.Metadata != v1Metadata)
                mismatches.Add($"{name}: v1 {v1Metadata} / v2 {result.Status} {result.Metadata} {result.Error}");
        }

        Assert.Empty(mismatches);
        Assert.Equal(expected.Count, compared);
    }

    [SamplePaksFact]
    public void Sample_paks_say_which_instrument_plays_each_part()
    {
        var paks = Directory.EnumerateFiles(SamplePaksFactAttribute.Directory!, "*.pak", SearchOption.AllDirectories)
            .ToDictionary(FuserPak.ShortName, StringComparer.OrdinalIgnoreCase);

        // Read from each part's SongCelData; the beat is drums and the lead is vocals in almost every song.
        var mj = _reader.Read(paks["TheyDontCareAboutUsMJ"]).Instruments;
        Assert.Equal(new SongInstruments("Drums", "None", "Strings", "Vocal"), mj);   // the author left the bass empty
        Assert.Equal(new SongInstruments("Drums", "Guitar", "Synth", "Vocal"), _reader.Read(paks["AB_dancingqueen_HazLaBaz"]).Instruments);
        Assert.All(paks.Values, pak => Assert.NotNull(_reader.Read(pak).Instruments?.Lead));
    }

    [SamplePaksFact]
    public void Sample_paks_can_be_read_in_parallel()
    {
        var paks = Directory.EnumerateFiles(SamplePaksFactAttribute.Directory!, "*.pak", SearchOption.AllDirectories).ToList();
        var sequential = paks.ToDictionary(p => p, _reader.Read);

        var parallel = new System.Collections.Concurrent.ConcurrentDictionary<string, SongReadResult>();
        Parallel.ForEach(paks, new ParallelOptions { MaxDegreeOfParallelism = 8 }, p => parallel[p] = _reader.Read(p));

        Assert.All(paks, p => Assert.Equal(sequential[p], parallel[p]));
    }

    private sealed record V1Reference(string short_name, string title, string artist, int year, string genre, string key, string mode, int bpm);
}
