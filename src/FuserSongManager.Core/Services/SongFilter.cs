using System.Globalization;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Services;

public enum StatusFilter
{
    All,
    Enabled,
    Disabled,
    /// <summary>Songs that failed to read, were read partially or have no .sig (RF-UI-04).</summary>
    WithProblems,
}

/// <summary>Search text plus filters, applied in memory to the whole library (RF-BUS-01, 02, 06).</summary>
public sealed record SongFilter
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions IgnoreCaseAndAccents = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static readonly SongFilter None = new();

    /// <summary>Every word must appear in the title, artist, author, notes or pak name, in any order.</summary>
    public string Text { get; init; } = "";

    public Genre? Genre { get; init; }
    public StatusFilter Status { get; init; } = StatusFilter.All;

    /// <summary>0 shows every song, including unrated ones.</summary>
    public int MinRating { get; init; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Genre is null && Status == StatusFilter.All && MinRating <= 0;

    public Func<Song, bool> ToPredicate()
    {
        var words = Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var genre = Genre;
        var status = Status;
        var minRating = MinRating;

        return song =>
        {
            if (genre is not null && song.Metadata.Genre != genre)
                return false;
            if (minRating > 0 && song.Rating < minRating)
                return false;
            if (!MatchesStatus(song, status))
                return false;
            foreach (var word in words)
            {
                if (!Contains(song.Metadata.Title, word) && !Contains(song.Metadata.Artist, word)
                    && !Contains(song.Author, word) && !Contains(song.Notes, word) && !Contains(song.FileName, word))
                    return false;
            }
            return true;
        };
    }

    public static bool HasProblem(Song song) => song.ReadStatus != ReadStatus.Ok || !song.HasSignature;

    private static bool MatchesStatus(Song song, StatusFilter status) => status switch
    {
        StatusFilter.Enabled => song.IsEnabled,
        StatusFilter.Disabled => !song.IsEnabled,
        StatusFilter.WithProblems => HasProblem(song),
        _ => true,
    };

    private static bool Contains(string? text, string word) =>
        text is not null && Compare.IndexOf(text, word, IgnoreCaseAndAccents) >= 0;
}
