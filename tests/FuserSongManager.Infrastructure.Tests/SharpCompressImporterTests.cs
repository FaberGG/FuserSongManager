using System.IO.Compression;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;
using FuserSongManager.Infrastructure.Files;

namespace FuserSongManager.Infrastructure.Tests;

public sealed class SharpCompressImporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly LibraryPaths _paths;
    private readonly string _workRoot;
    private readonly SharpCompressImporter _importer;

    public SharpCompressImporterTests()
    {
        _paths = LibraryPaths.FromFuserDirectory(_temp.File("Fuser"));
        Directory.CreateDirectory(_paths.EnabledDirectory);
        Directory.CreateDirectory(_paths.DisabledDirectory);
        _workRoot = _temp.File("work");
        _importer = new SharpCompressImporter(new LibraryScanner(), _workRoot);
    }

    public void Dispose() => _temp.Dispose();

    private string Zip(string name, params (string Entry, string Content)[] entries)
    {
        var path = _temp.File(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }
        return path;
    }

    private Task<ImportResult> Import(ImportOptions? options = null, params string[] sources) =>
        _importer.ImportAsync(sources, _paths, options ?? new ImportOptions());

    private string Enabled(string relative) => Path.Combine(_paths.EnabledDirectory, relative);
    private string Disabled(string relative) => Path.Combine(_paths.DisabledDirectory, relative);

    [Fact]
    public async Task Finds_every_complete_song_at_any_depth_and_reports_incomplete_ones()
    {
        var zip = Zip("pack.zip",
            ("A_P.pak", "pak A"), ("A_P.sig", "sig A"),
            ("Pack/Deep/B_P.pak", "pak B"), ("Pack/Deep/B_P.sig", "sig B"),
            ("Pack/C_P.pak", "pak C"),
            ("__MACOSX/Pack/._A_P.pak", "junk"),
            ("readme.txt", "hi"));

        var result = await Import(null, zip);

        Assert.Equal(["A_P", "B_P"], result.Imported.Select(s => s.FileName).Order());
        Assert.All(result.Imported, s => Assert.False(s.Replaced));
        Assert.Equal("pak B", File.ReadAllText(Enabled("B_P.pak")));   // flattened into the folder root
        Assert.Equal("sig B", File.ReadAllText(Enabled("B_P.sig")));
        var issue = Assert.Single(result.Issues);
        Assert.Equal((ImportIssueKind.MissingSignature, "C_P"), (issue.Kind, issue.FileName));
        Assert.False(File.Exists(Enabled("C_P.pak")));
    }

    [Fact]
    public async Task New_songs_can_go_to_the_disabled_folder()
    {
        var zip = Zip("a.zip", ("A_P.pak", "pak"), ("A_P.sig", "sig"));

        var result = await Import(new ImportOptions { Target = SongLocation.Disabled }, zip);

        Assert.Equal(SongLocation.Disabled, Assert.Single(result.Imported).Location);
        Assert.True(File.Exists(Disabled("A_P.pak")));
    }

    [Fact]
    public async Task Installed_song_is_replaced_where_it_is_and_not_duplicated_in_the_other_folder()
    {
        Directory.CreateDirectory(Disabled("Old"));
        File.WriteAllText(Disabled(Path.Combine("Old", "A_P.pak")), "old pak");
        File.WriteAllText(Disabled(Path.Combine("Old", "A_P.sig")), "old sig");
        var zip = Zip("a.zip", ("A_P.pak", "new pak"), ("A_P.sig", "new sig"));

        var result = await Import(new ImportOptions { Target = SongLocation.Enabled }, zip);

        var song = Assert.Single(result.Imported);
        Assert.True(song.Replaced);
        Assert.Equal(SongLocation.Disabled, song.Location);
        Assert.Equal("new pak", File.ReadAllText(Disabled(Path.Combine("Old", "A_P.pak"))));
        Assert.Equal("new sig", File.ReadAllText(Disabled(Path.Combine("Old", "A_P.sig"))));
        Assert.False(File.Exists(Enabled("A_P.pak")));
        Assert.Empty(Directory.EnumerateFiles(_paths.DisabledDirectory, "*.fsm*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Installed_song_is_kept_when_replacing_is_off()
    {
        File.WriteAllText(Enabled("A_P.pak"), "old pak");
        File.WriteAllText(Enabled("A_P.sig"), "old sig");
        var zip = Zip("a.zip", ("A_P.pak", "new pak"), ("A_P.sig", "new sig"));

        var result = await Import(new ImportOptions { ReplaceExisting = false }, zip);

        Assert.Empty(result.Imported);
        Assert.Equal(ImportIssueKind.AlreadyInstalled, Assert.Single(result.Issues).Kind);
        Assert.Equal("old pak", File.ReadAllText(Enabled("A_P.pak")));
    }

    [Fact]
    public async Task Failed_replacement_restores_the_previous_files()
    {
        File.WriteAllText(Enabled("A_P.pak"), "old pak");
        File.WriteAllText(Enabled("A_P.sig"), "old sig");
        var zip = Zip("a.zip", ("A_P.pak", "new pak"), ("A_P.sig", "new sig"));

        ImportResult result;
        // Locking the old .sig makes the swap fail half-way.
        using (new FileStream(Enabled("A_P.sig"), FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await Import(null, zip);

        Assert.Empty(result.Imported);
        Assert.Equal(ImportIssueKind.Failed, Assert.Single(result.Issues).Kind);
        Assert.Equal("old pak", File.ReadAllText(Enabled("A_P.pak")));
        Assert.Equal("old sig", File.ReadAllText(Enabled("A_P.sig")));
        Assert.Empty(Directory.EnumerateFiles(_paths.EnabledDirectory, "*.fsm*"));
    }

    [Fact]
    public async Task Same_song_in_two_sources_is_installed_once()
    {
        var first = Zip("1.zip", ("A_P.pak", "first"), ("A_P.sig", "sig"));
        var second = Zip("2.zip", ("x/A_P.pak", "second"), ("x/A_P.sig", "sig"));

        var result = await Import(null, first, second);

        Assert.Single(result.Imported);
        Assert.Equal(ImportIssueKind.DuplicateInImport, Assert.Single(result.Issues).Kind);
        Assert.Equal("first", File.ReadAllText(Enabled("A_P.pak")));
    }

    [Fact]
    public async Task Several_copies_of_a_song_in_one_archive_are_skipped_as_ambiguous()
    {
        var zip = Zip("a.zip", ("v1/A_P.pak", "1"), ("v1/A_P.sig", "1"), ("v2/A_P.pak", "2"), ("v2/A_P.sig", "2"));

        var result = await Import(null, zip);

        Assert.Empty(result.Imported);
        Assert.Equal(ImportIssueKind.Ambiguous, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task Loose_pak_needs_its_sig_alongside()
    {
        Directory.CreateDirectory(_temp.File("loose"));
        File.WriteAllText(_temp.File(Path.Combine("loose", "A_P.pak")), "pak");
        File.WriteAllText(_temp.File(Path.Combine("loose", "A_P.sig")), "sig");
        File.WriteAllText(_temp.File(Path.Combine("loose", "B_P.pak")), "pak");

        var result = await Import(null, _temp.File(Path.Combine("loose", "A_P.pak")), _temp.File(Path.Combine("loose", "B_P.pak")));

        Assert.Equal("A_P", Assert.Single(result.Imported).FileName);
        Assert.Equal(ImportIssueKind.MissingSignature, Assert.Single(result.Issues).Kind);
        Assert.True(File.Exists(_temp.File(Path.Combine("loose", "A_P.pak"))));   // copied, not moved
    }

    [Fact]
    public async Task Unreadable_or_empty_archives_are_reported()
    {
        var notArchive = _temp.File("fake.zip");
        File.WriteAllBytes(notArchive, Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray());
        var noSongs = Zip("empty.zip", ("readme.txt", "nothing here"));

        var result = await Import(null, notArchive, noSongs);

        Assert.Empty(result.Imported);
        Assert.Equal([ImportIssueKind.UnreadableArchive, ImportIssueKind.NoSongsFound], result.Issues.Select(i => i.Kind));
    }

    [Theory]
    [InlineData("songs.rar")]
    [InlineData("songs-solid.rar")]
    public async Task Rar_archives_are_supported(string fixture)
    {
        var result = await Import(null, Path.Combine("TestData", fixture));

        Assert.Equal(["RarSongA_P", "RarSongB_P"], result.Imported.Select(s => s.FileName).Order());
        Assert.Equal("PAK-RAR-B", File.ReadAllText(Enabled("RarSongB_P.pak")));
        Assert.Equal("SIG-RAR-B", File.ReadAllText(Enabled("RarSongB_P.sig")));
    }

    [Fact]
    public async Task Seven_zip_archives_are_supported()
    {
        var result = await Import(null, Path.Combine("TestData", "songs.7z"));

        Assert.Equal("SevenSong_P", Assert.Single(result.Imported).FileName);
        Assert.Equal("PAK-7Z", File.ReadAllText(Enabled("SevenSong_P.pak")));
    }

    [Fact]
    public async Task Temporary_files_are_always_cleaned_up()
    {
        var good = Zip("good.zip", ("A_P.pak", "pak"), ("A_P.sig", "sig"));
        var bad = _temp.File("bad.zip");
        File.WriteAllText(bad, "not an archive");

        await Import(null, good, bad);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _importer.ImportAsync([good], _paths, new ImportOptions(), ct: new CancellationToken(canceled: true)));

        Assert.True(!Directory.Exists(_workRoot) || !Directory.EnumerateFileSystemEntries(_workRoot).Any());
    }

    [Fact]
    public async Task Progress_is_reported_per_source()
    {
        var zip = Zip("a.zip", ("A_P.pak", "pak"), ("A_P.sig", "sig"));
        var reports = new List<ImportProgress>();

        await _importer.ImportAsync([zip], _paths, new ImportOptions(), new InlineProgress<ImportProgress>(reports.Add));

        Assert.Equal(["Reading", "Installing", "Done"], reports.Select(r => r.Stage));
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
