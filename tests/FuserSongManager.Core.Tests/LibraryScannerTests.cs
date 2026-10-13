using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;

namespace FuserSongManager.Core.Tests;

public sealed class LibraryScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fsm-scanner-" + Guid.NewGuid().ToString("N"));
    private readonly LibraryPaths _paths;

    public LibraryScannerTests() => _paths = LibraryPaths.FromFuserDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void CreateSong(SongLocation location, string relativePakPath, bool withSig = true)
    {
        var pak = Path.Combine(_paths.RootOf(location), relativePakPath);
        Directory.CreateDirectory(Path.GetDirectoryName(pak)!);
        File.WriteAllBytes(pak, new byte[123]);
        if (withSig)
            File.WriteAllBytes(Path.ChangeExtension(pak, ".sig"), new byte[1]);
    }

    [Fact]
    public void Finds_paks_in_both_folders_including_subfolders()
    {
        CreateSong(SongLocation.Enabled, "A_P.pak");
        CreateSong(SongLocation.Enabled, Path.Combine("Pack 1", "deep", "B_P.pak"));
        CreateSong(SongLocation.Disabled, Path.Combine("Old", "C_P.pak"));

        var scan = new LibraryScanner().Scan(_paths);

        Assert.Equal(["A_P", "B_P", "C_P"], scan.Paks.Select(p => p.FileName).Order());
        var b = scan.Paks.Single(p => p.FileName == "B_P");
        Assert.Equal(SongLocation.Enabled, b.Location);
        Assert.Equal(Path.Combine("Pack 1", "deep", "B_P.pak"), b.RelativePath);
        Assert.Equal(123, b.Size);
        Assert.Equal(SongLocation.Disabled, scan.Paks.Single(p => p.FileName == "C_P").Location);
    }

    [Fact]
    public void Detects_missing_signature()
    {
        CreateSong(SongLocation.Enabled, "A_P.pak", withSig: true);
        CreateSong(SongLocation.Enabled, "B_P.pak", withSig: false);

        var scan = new LibraryScanner().Scan(_paths);

        Assert.True(scan.Paks.Single(p => p.FileName == "A_P").HasSignature);
        Assert.False(scan.Paks.Single(p => p.FileName == "B_P").HasSignature);
    }

    [Fact]
    public void Same_pak_in_both_folders_keeps_the_enabled_one_and_reports_the_duplicate()
    {
        CreateSong(SongLocation.Enabled, "A_P.pak");
        CreateSong(SongLocation.Disabled, "A_P.pak");

        var scan = new LibraryScanner().Scan(_paths);

        Assert.Equal(SongLocation.Enabled, Assert.Single(scan.Paks).Location);
        Assert.Equal(SongLocation.Disabled, Assert.Single(scan.Duplicates).Location);
    }

    [Fact]
    public void Missing_folders_give_an_empty_library()
    {
        var scan = new LibraryScanner().Scan(_paths);

        Assert.Empty(scan.Paks);
    }
}
