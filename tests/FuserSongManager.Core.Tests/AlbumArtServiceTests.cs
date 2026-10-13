using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;

namespace FuserSongManager.Core.Tests;

public sealed class AlbumArtServiceTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "fsm-art-" + Guid.NewGuid().ToString("N"));
    private readonly FakeMedia _media = new();

    public void Dispose()
    {
        if (Directory.Exists(_cache))
            Directory.Delete(_cache, recursive: true);
    }

    private sealed class FakeMedia : ISongMediaReader
    {
        public Dictionary<string, byte[]?> Art { get; } = [];
        public int Reads { get; private set; }
        public bool Throw { get; set; }

        public byte[]? ReadAlbumArt(string pakPath)
        {
            Reads++;
            if (Throw) throw new InvalidDataException("bad pak");
            return Art.GetValueOrDefault(pakPath);
        }

        public SongAudio ReadAudio(string pakPath, SongMode? mode) => throw new NotSupportedException();
    }

    private static Song Song(long size = 100, string name = "A_P") =>
        new() { FileName = name, FileSize = size, FileModifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void Art_is_read_once_then_served_from_the_cache()
    {
        _media.Art["a.pak"] = [1, 2, 3];
        var service = new AlbumArtService(_media, _cache);

        Assert.Equal([1, 2, 3], service.Get(Song(), "a.pak"));
        Assert.Equal([1, 2, 3], new AlbumArtService(_media, _cache).Get(Song(), "a.pak"));   // survives a restart

        Assert.Equal(1, _media.Reads);
    }

    [Fact]
    public void A_replaced_pak_is_read_again_and_its_old_entry_removed()
    {
        _media.Art["a.pak"] = [1];
        var service = new AlbumArtService(_media, _cache);
        service.Get(Song(size: 100), "a.pak");

        _media.Art["a.pak"] = [2];
        Assert.Equal([2], service.Get(Song(size: 200), "a.pak"));

        Assert.Single(Directory.GetFiles(_cache));
    }

    [Fact]
    public void Paks_without_art_are_remembered()
    {
        var service = new AlbumArtService(_media, _cache);

        Assert.Null(service.Get(Song(), "none.pak"));
        Assert.Null(service.Get(Song(), "none.pak"));

        Assert.Equal(1, _media.Reads);
    }

    [Fact]
    public void Unreadable_paks_show_no_art_and_are_retried()
    {
        _media.Throw = true;
        var service = new AlbumArtService(_media, _cache);

        Assert.Null(service.Get(Song(), "bad.pak"));
        Assert.Null(service.Get(Song(), "bad.pak"));

        Assert.Equal(2, _media.Reads);
    }

    [Fact]
    public void Songs_never_scanned_by_v2_are_not_cached()
    {
        _media.Art["a.pak"] = [9];
        var service = new AlbumArtService(_media, _cache);
        var unscanned = new Song { FileName = "A_P" };

        service.Get(unscanned, "a.pak");
        service.Get(unscanned, "a.pak");

        Assert.Equal(2, _media.Reads);
        Assert.False(Directory.Exists(_cache));
    }
}
