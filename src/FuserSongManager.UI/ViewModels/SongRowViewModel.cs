using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;

namespace FuserSongManager.UI.ViewModels;

/// <summary>The instrument icon of one part, as shown on a tile.</summary>
public sealed record PartIcon(StemKind Kind, string? Instrument);

/// <summary>One song tile. Read-only view of a <see cref="Core.Models.Song"/>; replaced as a whole when the song changes.</summary>
public sealed partial class SongRowViewModel(Song song) : ViewModelBase
{
    public Song Song { get; private set; } = song;

    /// <summary>Returns true if anything changed. Raises a change for every property at once.</summary>
    public bool Update(Song song)
    {
        if (song == Song)
            return false;
        var artChanged = song.FileSize != Song.FileSize || song.FileModifiedUtc != Song.FileModifiedUtc;
        Song = song;
        if (artChanged && ThumbnailRequested)
        {
            // The pak was replaced while its tile was on screen: show the new art.
            ResetThumbnail();
            Loader?.Request(this);
        }
        OnPropertyChanged(string.Empty);
        return true;
    }

    public string FileName => Song.FileName;

    /// <summary>Falls back to the pak name so unread songs are still identifiable.</summary>
    public string Title => Song.Metadata.Title ?? Song.FileName;
    public string TitleSortKey => SongSorting.SortKey(Title);
    public string Artist => Song.Metadata.Artist ?? "";
    public string ArtistSortKey => SongSorting.SortKey(Song.Metadata.Artist);
    public int? Year => Song.Metadata.Year;
    public string Genre => Display.Genre(Song.Metadata.Genre);
    public string Key => Display.Key(Song.Metadata.Key);
    public int? KeySortValue => (int?)Song.Metadata.Key;
    public string Mode => Display.Mode(Song.Metadata.Mode);
    public int? Bpm => Song.Metadata.Bpm;
    public string State => Display.Location(Song.Location);
    public bool IsEnabled => Song.IsEnabled;
    public bool IsDisabled => !Song.IsEnabled;
    public int Rating => Song.Rating;
    /// <summary>Empty for unrated songs so rated ones stand out.</summary>
    public string Stars => Song.Rating > 0 ? Display.Stars(Song.Rating) : "";
    public string Author => Song.Author;

    public bool HasProblem => SongFilter.HasProblem(Song);
    public string? Problem => Display.Problem(Song);

    /// <summary>"Rock · 2003" for the tile's hover line; empty parts are left out.</summary>
    public string GenreAndYear => string.Join(" · ", new[] { Genre, Year?.ToString() }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>The four parts' instruments, or none when the pak has not been read for them yet.</summary>
    public IReadOnlyList<PartIcon> Parts =>
        Song.Instruments is { } instruments && instruments != SongInstruments.Unknown
            ? [.. Enum.GetValues<StemKind>().Select(k => new PartIcon(k, instruments.For(k)))]
            : [];

    /// <summary>Shown with a check mark; several tiles can be selected with Ctrl or Shift.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    // ---- Thumbnail -------------------------------------------------------------------------

    /// <summary>The album art at tile size, loaded the first time the tile is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial Bitmap? Thumbnail { get; private set; }

    /// <summary>True once the art was looked for (it may not exist).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial bool ThumbnailLoaded { get; private set; }

    /// <summary>The music-note placeholder, for songs without art (not while the art is still loading).</summary>
    public bool ShowPlaceholder => ThumbnailLoaded && Thumbnail is null;

    internal bool ThumbnailRequested { get; set; }

    internal Services.ThumbnailLoader? Loader { get; init; }

    /// <summary>Called by the tile when it comes into view.</summary>
    public void RequestThumbnail() => Loader?.Request(this);

    internal void SetThumbnail(Bitmap? bitmap)
    {
        Thumbnail = bitmap;
        ThumbnailLoaded = true;
    }

    private void ResetThumbnail()
    {
        ThumbnailRequested = false;
        Thumbnail = null;
        ThumbnailLoaded = false;
    }
}
