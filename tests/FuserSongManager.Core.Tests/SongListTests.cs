using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;

namespace FuserSongManager.Core.Tests;

public class SongSortingTests
{
    [Theory]
    [InlineData("The Killers", "Killers")]
    [InlineData("the beatles", "beatles")]
    [InlineData("A Tribe Called Quest", "Tribe Called Quest")]
    [InlineData("  The  Weeknd ", "Weeknd")]
    [InlineData("Africa", "Africa")]
    [InlineData("Theme", "Theme")]
    [InlineData("A", "A")]
    [InlineData("The", "The")]
    [InlineData(null, "")]
    public void Sort_key_ignores_leading_the_and_a(string? text, string expected) =>
        Assert.Equal(expected, SongSorting.SortKey(text));
}

public class SongFilterTests
{
    private static Song Song(
        string title = "Title", string artist = "Artist", Genre? genre = null, int rating = 0,
        SongLocation location = SongLocation.Enabled, ReadStatus status = ReadStatus.Ok, bool sig = true,
        string author = "", string notes = "", string fileName = "Song_P") => new()
        {
            FileName = fileName,
            Metadata = SongMetadata.Empty with { Title = title, Artist = artist, Genre = genre },
            Rating = rating,
            Location = location,
            ReadStatus = status,
            HasSignature = sig,
            Author = author,
            Notes = notes,
        };

    private static bool Matches(SongFilter filter, Song song) => filter.ToPredicate()(song);

    [Fact]
    public void Empty_filter_matches_everything()
    {
        Assert.True(SongFilter.None.IsEmpty);
        Assert.True(Matches(SongFilter.None, Song(title: null!, artist: null!)));
    }

    [Theory]
    [InlineData("killers", true)]
    [InlineData("BRIGHTSIDE", true)]       // case ignored
    [InlineData("brightsides", false)]
    [InlineData("mr bright", true)]       // words in any field, any order
    [InlineData("bright killers", true)]
    [InlineData("bright beatles", false)] // every word must match
    [InlineData("cafe", true)]            // accents ignored
    [InlineData("custom_song", true)]     // pak name
    [InlineData("pakmaker", true)]        // author
    [InlineData("parties", true)]         // notes
    public void Text_search_covers_title_artist_author_notes_and_pak_name(string text, bool expected)
    {
        var song = Song(title: "Mr. Brightside", artist: "The Killers", author: "PakMaker", notes: "Great for parties · Café", fileName: "custom_song_P");
        Assert.Equal(expected, Matches(new SongFilter { Text = text }, song));
    }

    [Fact]
    public void Genre_and_minimum_rating_filters_combine()
    {
        var filter = new SongFilter { Genre = Genre.Rock, MinRating = 3 };

        Assert.True(Matches(filter, Song(genre: Genre.Rock, rating: 4)));
        Assert.False(Matches(filter, Song(genre: Genre.Rock, rating: 2)));
        Assert.False(Matches(filter, Song(genre: Genre.Pop, rating: 5)));
        Assert.False(Matches(filter, Song(genre: null, rating: 5)));
    }

    [Fact]
    public void Status_filter_separates_enabled_disabled_and_problems()
    {
        var enabled = Song();
        var disabled = Song(location: SongLocation.Disabled);
        var failed = Song(status: ReadStatus.Failed);
        var noSig = Song(location: SongLocation.Disabled, sig: false);

        Assert.True(Matches(new SongFilter { Status = StatusFilter.Enabled }, enabled));
        Assert.False(Matches(new SongFilter { Status = StatusFilter.Enabled }, disabled));
        Assert.True(Matches(new SongFilter { Status = StatusFilter.Disabled }, disabled));
        Assert.Equal(
            [false, false, true, true],
            new[] { enabled, disabled, failed, noSig }.Select(new SongFilter { Status = StatusFilter.WithProblems }.ToPredicate()));
    }
}

public sealed class FuserInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fsm-install-" + Guid.NewGuid().ToString("N"));

    public FuserInstallTests() => Directory.CreateDirectory(Path.Combine(_root, "Game", "Fuser", "Content", "Paks", "custom_songs", "Pack"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Install_folder_is_found_from_itself_or_any_folder_inside_it()
    {
        var install = Path.Combine(_root, "Game");

        Assert.True(FuserInstall.IsInstallFolder(install));
        Assert.Equal(install, FuserInstall.FindInstallFolder(install));
        Assert.Equal(install, FuserInstall.FindInstallFolder(Path.Combine(install, "Fuser", "Content", "Paks")));
        Assert.Equal(install, FuserInstall.FindInstallFolder(Path.Combine(install, "Fuser", "Content", "Paks", "custom_songs", "Pack")));
    }

    [Fact]
    public void Unrelated_folders_are_rejected()
    {
        Assert.False(FuserInstall.IsInstallFolder(_root));
        Assert.Null(FuserInstall.FindInstallFolder(_root));
        Assert.Null(FuserInstall.FindInstallFolder(""));
        Assert.Null(FuserInstall.FindInstallFolder(Path.Combine(_root, "does-not-exist")));
    }
}
