using FuserSongManager.Core.Models;
using FuserSongManager.Infrastructure.Files;

namespace FuserSongManager.Infrastructure.Tests;

public sealed class SongFileServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly LibraryPaths _paths;
    private readonly SongFileService _service = new();

    public SongFileServiceTests() => _paths = LibraryPaths.FromFuserDirectory(_temp.Path);

    public void Dispose() => _temp.Dispose();

    private Song CreateSong(SongLocation location, string relativePak, bool withSig = true)
    {
        var pak = Path.Combine(_paths.RootOf(location), relativePak);
        Directory.CreateDirectory(Path.GetDirectoryName(pak)!);
        File.WriteAllText(pak, "pak");
        if (withSig)
            File.WriteAllText(Path.ChangeExtension(pak, ".sig"), "sig");
        return new Song { FileName = Path.GetFileNameWithoutExtension(pak), Location = location, RelativePath = relativePak, HasSignature = withSig };
    }

    private string At(SongLocation location, string relative) => Path.Combine(_paths.RootOf(location), relative);

    [Fact]
    public void Moves_pak_and_sig_together_keeping_the_subfolder()
    {
        var song = CreateSong(SongLocation.Enabled, Path.Combine("Pack", "A_P.pak"));

        var result = _service.Move(song, SongLocation.Disabled, _paths);

        Assert.Equal(StateChangeOutcome.Moved, result.Outcome);
        Assert.True(File.Exists(At(SongLocation.Disabled, Path.Combine("Pack", "A_P.pak"))));
        Assert.True(File.Exists(At(SongLocation.Disabled, Path.Combine("Pack", "A_P.sig"))));
        Assert.False(File.Exists(At(SongLocation.Enabled, Path.Combine("Pack", "A_P.pak"))));
        Assert.False(Directory.Exists(At(SongLocation.Enabled, "Pack")));   // left empty → removed
        Assert.True(Directory.Exists(_paths.EnabledDirectory));             // the root itself stays
    }

    [Fact]
    public void Source_folder_with_other_files_is_kept()
    {
        var song = CreateSong(SongLocation.Enabled, Path.Combine("Pack", "A_P.pak"));
        CreateSong(SongLocation.Enabled, Path.Combine("Pack", "B_P.pak"));

        _service.Move(song, SongLocation.Disabled, _paths);

        Assert.True(File.Exists(At(SongLocation.Enabled, Path.Combine("Pack", "B_P.pak"))));
    }

    [Fact]
    public void Enabling_without_sig_is_refused_and_nothing_moves()
    {
        var song = CreateSong(SongLocation.Disabled, "A_P.pak", withSig: false);

        var result = _service.Move(song, SongLocation.Enabled, _paths);

        Assert.Equal(StateChangeOutcome.MissingSignature, result.Outcome);
        Assert.Contains("A_P.sig", result.Message);
        Assert.True(File.Exists(At(SongLocation.Disabled, "A_P.pak")));
    }

    [Fact]
    public void Disabling_without_sig_moves_the_pak()
    {
        var song = CreateSong(SongLocation.Enabled, "A_P.pak", withSig: false);

        var result = _service.Move(song, SongLocation.Disabled, _paths);

        Assert.Equal(StateChangeOutcome.Moved, result.Outcome);
        Assert.True(File.Exists(At(SongLocation.Disabled, "A_P.pak")));
    }

    [Fact]
    public void Existing_file_in_destination_is_never_overwritten()
    {
        var song = CreateSong(SongLocation.Disabled, "A_P.pak");
        Directory.CreateDirectory(_paths.EnabledDirectory);
        File.WriteAllText(At(SongLocation.Enabled, "A_P.sig"), "other sig");

        var result = _service.Move(song, SongLocation.Enabled, _paths);

        Assert.Equal(StateChangeOutcome.DestinationExists, result.Outcome);
        Assert.Equal("other sig", File.ReadAllText(At(SongLocation.Enabled, "A_P.sig")));
        Assert.True(File.Exists(At(SongLocation.Disabled, "A_P.pak")));
        Assert.True(File.Exists(At(SongLocation.Disabled, "A_P.sig")));
    }

    [Fact]
    public void Failure_moving_the_sig_puts_the_pak_back()
    {
        var song = CreateSong(SongLocation.Enabled, "A_P.pak");
        // An open handle without FileShare.Delete makes the .sig impossible to move on Windows.
        using (new FileStream(At(SongLocation.Enabled, "A_P.sig"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = _service.Move(song, SongLocation.Disabled, _paths);

            Assert.Equal(StateChangeOutcome.Failed, result.Outcome);
            Assert.Contains("undone", result.Message);
        }

        Assert.True(File.Exists(At(SongLocation.Enabled, "A_P.pak")));
        Assert.True(File.Exists(At(SongLocation.Enabled, "A_P.sig")));
        Assert.False(File.Exists(At(SongLocation.Disabled, "A_P.pak")));
    }

    [Fact]
    public void Missing_source_file_fails_without_side_effects()
    {
        var song = new Song { FileName = "Gone_P", Location = SongLocation.Enabled, RelativePath = "Gone_P.pak" };

        var result = _service.Move(song, SongLocation.Disabled, _paths);

        Assert.Equal(StateChangeOutcome.Failed, result.Outcome);
        Assert.Contains("Rescan", result.Message);
    }

    [Fact]
    public void Paths_escaping_the_songs_folders_are_refused()
    {
        File.WriteAllText(_temp.File("outside_P.pak"), "pak");
        var song = new Song { FileName = "outside_P", Location = SongLocation.Enabled, RelativePath = Path.Combine("..", "..", "..", "..", "outside_P.pak") };

        var result = _service.Move(song, SongLocation.Disabled, _paths);

        Assert.Equal(StateChangeOutcome.Failed, result.Outcome);
        Assert.True(File.Exists(_temp.File("outside_P.pak")));
    }

    [Fact]
    public void Clearing_the_unlocked_songs_cache_deletes_both_files_and_tolerates_absence()
    {
        Directory.CreateDirectory(_paths.PaksDirectory);
        File.WriteAllText(Path.Combine(_paths.PaksDirectory, "customSongsUnlocked_P.pak"), "x");
        File.WriteAllText(Path.Combine(_paths.PaksDirectory, "customSongsUnlocked_P.sig"), "x");

        _service.ClearUnlockedSongsCache(_paths);
        _service.ClearUnlockedSongsCache(_paths);

        Assert.Empty(Directory.EnumerateFiles(_paths.PaksDirectory));
    }
}
