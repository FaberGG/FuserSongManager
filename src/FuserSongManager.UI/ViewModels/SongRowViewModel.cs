using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;

namespace FuserSongManager.UI.ViewModels;

/// <summary>One row of the song list. Read-only view of a <see cref="Core.Models.Song"/>; replaced as a whole when the song changes.</summary>
public sealed class SongRowViewModel(Song song) : ViewModelBase
{
    public Song Song { get; private set; } = song;

    /// <summary>Returns true if anything changed. Raises a change for every column at once.</summary>
    public bool Update(Song song)
    {
        if (song == Song)
            return false;
        Song = song;
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
    public int Rating => Song.Rating;
    /// <summary>Empty for unrated songs so rated ones stand out in the list.</summary>
    public string Stars => Song.Rating > 0 ? Display.Stars(Song.Rating) : "";
    public string Author => Song.Author;

    public bool HasProblem => SongFilter.HasProblem(Song);
    public string? Problem => Display.Problem(Song);

    /// <summary>Disabled songs are dimmed so the enabled set stands out.</summary>
    public double RowOpacity => Song.IsEnabled ? 1.0 : 0.55;
}
