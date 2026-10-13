using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuserSongManager.Core.Models;
using FuserSongManager.UI.Services;

namespace FuserSongManager.UI.ViewModels;

public sealed record StarSlot(int Value, bool Filled)
{
    public string Glyph => Filled ? "★" : "☆";
}

/// <summary>
/// Detail panel of the selected song (RF-DET-01, 02, 05). Rating, notes and author are saved as soon as
/// they change: the rating on click, the text fields when they lose focus (RF-USR-01..04).
/// </summary>
public sealed partial class SongDetailViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly bool _loading;
    private readonly CancellationTokenSource _detached = new();

    public SongDetailViewModel(SongRowViewModel row, AppServices services, IDialogService dialogs, IShellService shell)
    {
        Row = row;
        _services = services;
        _dialogs = dialogs;
        _shell = shell;

        _loading = true;
        Rating = row.Song.Rating;
        Notes = row.Song.Notes;
        Author = row.Song.Author;
        _loading = false;

        row.PropertyChanged += OnRowChanged;

        if (services.Media is { } media && FullPath is not null)
        {
            ArtMessage = "Loading album art…";
            _ = LoadAlbumArtAsync(_detached.Token);
            if (services.CreatePlayer is { } createPlayer)
            {
                // The path is resolved when play is pressed: the song may have been enabled or disabled meanwhile.
                Player = new PlayerViewModel(row.FileName,
                    () => media.ReadAudio(FullPath ?? throw new InvalidOperationException("The song's file is not known; rescan the library."), Song.Metadata.Mode),
                    createPlayer, services.Log);
            }
        }
        else
        {
            ArtMessage = FullPath is null ? "Rescan the library to see the album art." : null;
        }
    }

    /// <summary>Stops following the row, loading the art and playing, once the panel shows another song.</summary>
    public void Detach()
    {
        Row.PropertyChanged -= OnRowChanged;
        _detached.Cancel();
        Player?.Dispose();
    }

    /// <summary>The song preview, or null when audio is not available (RF-PEN-13).</summary>
    public PlayerViewModel? Player { get; }

    /// <summary>The album art from the pak (RF-PEN-14), or null with <see cref="ArtMessage"/> saying why.</summary>
    [ObservableProperty]
    public partial Avalonia.Media.Imaging.Bitmap? AlbumArt { get; private set; }

    [ObservableProperty]
    public partial string? ArtMessage { get; private set; }

    /// <summary>Completes when the art has been loaded (or found missing); for tests.</summary>
    public Task ArtLoaded { get; private set; } = Task.CompletedTask;

    private Task LoadAlbumArtAsync(CancellationToken ct) => ArtLoaded = LoadAlbumArtCoreAsync(ct);

    private async Task LoadAlbumArtCoreAsync(CancellationToken ct)
    {
        try
        {
            var song = Song;
            var path = FullPath!;
            var png = await Task.Run(() => _services.AlbumArt!.Get(song, path), ct);
            if (ct.IsCancellationRequested)
                return;
            if (png is null)
            {
                ArtMessage = "This song has no album art.";
                return;
            }
            AlbumArt = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
            ArtMessage = null;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _services.Log.Warning(ex, "Album art of {FileName} could not be shown", FileName);
            ArtMessage = "The album art could not be shown.";
        }
    }

    // A rescan may update the row while it is shown. Only the read-only fields are refreshed: raising
    // a change for Notes or Author would wipe text the user is still typing.
    private static readonly string[] ReadOnlyFields =
    [
        nameof(Title), nameof(Artist), nameof(Year), nameof(Genre), nameof(Key), nameof(Mode), nameof(Bpm),
        nameof(State), nameof(FileName), nameof(ReadStatus), nameof(Problem), nameof(HasProblem), nameof(Location), nameof(FullPath),
    ];

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (var name in ReadOnlyFields)
            OnPropertyChanged(name);
    }

    public SongRowViewModel Row { get; }
    private Song Song => Row.Song;

    public string Title => Row.Title;
    public string Artist => Row.Artist;
    public string Year => Song.Metadata.Year?.ToString() ?? "—";
    public string Genre => Or(Row.Genre);
    public string Key => Or(Row.Key);
    public string Mode => Or(Row.Mode);
    public string Bpm => Song.Metadata.Bpm?.ToString() ?? "—";
    public string State => Row.State;
    public string FileName => Song.FileName;
    public string ReadStatus => Display.ReadStatus(Song.ReadStatus);
    public string? Problem => Row.Problem;
    public bool HasProblem => Problem is not null;

    public string Location => FullPath ?? "Not scanned yet";

    /// <summary>Absolute path of the .pak, or null if unknown (song imported from v1 and not scanned).</summary>
    public string? FullPath =>
        Song.RelativePath is { } relative && _services.Paths is { } paths
            ? Path.Combine(paths.RootOf(Song.Location), relative)
            : null;

    [ObservableProperty]
    public partial int Rating { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; }

    [ObservableProperty]
    public partial string Author { get; set; }

    public string Stars => Display.Stars(Rating);

    /// <summary>Five clickable stars, filled up to the rating.</summary>
    public IReadOnlyList<StarSlot> StarSlots => [.. Enumerable.Range(1, 5).Select(n => new StarSlot(n, n <= Rating))];

    /// <summary>Clicking the current rating again clears it.</summary>
    [RelayCommand]
    private void SetRating(int stars) => Rating = Rating == stars ? 0 : Math.Clamp(stars, 0, 5);

    [RelayCommand]
    private void OpenFolder()
    {
        if (FullPath is { } path)
            _shell.ShowInFileManager(path);
    }

    partial void OnRatingChanged(int value)
    {
        OnPropertyChanged(nameof(Stars));
        OnPropertyChanged(nameof(StarSlots));
        _ = SaveAsync();
    }

    partial void OnNotesChanged(string value) => _ = SaveAsync();

    partial void OnAuthorChanged(string value) => _ = SaveAsync();

    /// <summary>The last save, so tests and the window can wait for it.</summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    private Task SaveAsync()
    {
        if (_loading)
            return Task.CompletedTask;
        return PendingSave = SaveCoreAsync(Rating, Notes ?? "", (Author ?? "").Trim());
    }

    private async Task SaveCoreAsync(int rating, string notes, string author)
    {
        try
        {
            await _services.Repository.UpdateUserDataAsync(Song.FileName, rating, notes, author);
            Row.Update(Row.Song with { Rating = rating, Notes = notes, Author = author });
        }
        catch (Exception ex)
        {
            _services.Log.Error(ex, "Could not save rating, notes or author of {FileName}", Song.FileName);
            await _dialogs.ShowMessageAsync("Could not save", $"The changes to {Song.FileName} could not be saved.\n\n{ex.Message}");
        }
    }

    private static string Or(string value) => value.Length == 0 ? "—" : value;
}
