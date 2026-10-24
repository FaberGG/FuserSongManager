using System.Collections.ObjectModel;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;
using FuserSongManager.UI.Services;

namespace FuserSongManager.UI.ViewModels;

public sealed record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public enum SongSort
{
    Title,
    Artist,
    Genre,
    Year,
    Bpm,
    Rating,
}

/// <summary>One of the tabs over the library (All, Enabled, Disabled, With problems) with its song count.</summary>
public sealed partial class StatusTabViewModel(string label, StatusFilter value, Action<StatusFilter> select) : ViewModelBase
{
    public string Label { get; } = label;
    public StatusFilter Value { get; } = value;

    [ObservableProperty]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The problems tab only shows up when some song has one.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [RelayCommand]
    private void Select() => select(Value);
}

/// <summary>One row of tiles. The grid is a list of rows so only the rows on screen are created.</summary>
public sealed record TileRow(IReadOnlyList<SongRowViewModel> Songs, int Columns);

/// <summary>
/// Main window: the song library as a grid of album art with search and filters, the detail of the selected
/// song, and the actions that change files (enable, disable, import), which run one at a time and never while
/// the game is open.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan GamePollInterval = TimeSpan.FromSeconds(2);

    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly List<SongRowViewModel> _rows = [];
    private readonly Dictionary<string, SongRowViewModel> _rowsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _searchDelay;
    private IReadOnlyList<SongRowViewModel> _selection = [];
    private SongRowViewModel? _anchor;
    private bool _resettingFilters;

    public MainViewModel(AppServices services, IDialogService dialogs, IShellService shell)
    {
        _services = services;
        _dialogs = dialogs;
        _shell = shell;
        Thumbnails = new ThumbnailLoader(services);

        GenreOptions = [new("All genres", null), .. Enum.GetValues<Genre>().Select(g => new Option<Genre?>(Display.Genre(g), g))];
        StatusOptions =
        [
            new("All", StatusFilter.All),
            new("Enabled", StatusFilter.Enabled),
            new("Disabled", StatusFilter.Disabled),
            new("With problems", StatusFilter.WithProblems),
        ];
        StatusTabs = [.. StatusOptions.Select(o => new StatusTabViewModel(o.Label, o.Value, v => SelectedStatus = StatusOptions.First(x => x.Value == v)))];
        RatingOptions = [new("Any rating", 0), .. Enumerable.Range(1, 5).Select(n => new Option<int>(Display.Stars(n) + (n < 5 ? " or more" : ""), n))];
        SortOptions =
        [
            new("Sorted by Title", SongSort.Title),
            new("Sorted by Artist", SongSort.Artist),
            new("Sorted by Genre", SongSort.Genre),
            new("Sorted by Year", SongSort.Year),
            new("Sorted by BPM", SongSort.Bpm),
            new("Sorted by Rating", SongSort.Rating),
        ];

        SelectedGenre = GenreOptions[0];
        SelectedStatus = StatusOptions[0];
        SelectedRating = RatingOptions[0];
        SelectedSort = SortOptions[0];
        SearchText = "";
        IsAutoplayOn = services.Settings.AutoplayPreview;
    }

    public ThumbnailLoader Thumbnails { get; }

    // ---- List, search, filters and sorting -------------------------------------------------

    /// <summary>The songs that pass the search and filters, in the chosen order.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SongRowViewModel> VisibleSongs { get; private set; } = [];

    /// <summary><see cref="VisibleSongs"/> cut into rows of <see cref="Columns"/> tiles.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TileRow> TileRows { get; private set; } = [];

    /// <summary>Tiles per row; the window sets it from its width.</summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 6;

    partial void OnColumnsChanged(int value) => BuildTileRows();

    public IReadOnlyList<Option<Genre?>> GenreOptions { get; }
    public IReadOnlyList<Option<StatusFilter>> StatusOptions { get; }
    public IReadOnlyList<StatusTabViewModel> StatusTabs { get; }
    public IReadOnlyList<Option<int>> RatingOptions { get; }
    public IReadOnlyList<Option<SongSort>> SortOptions { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial Option<Genre?> SelectedGenre { get; set; }

    [ObservableProperty]
    public partial Option<StatusFilter> SelectedStatus { get; set; }

    [ObservableProperty]
    public partial Option<int> SelectedRating { get; set; }

    [ObservableProperty]
    public partial Option<SongSort> SelectedSort { get; set; }

    [ObservableProperty]
    public partial string CountText { get; private set; } = "";

    public SongFilter CurrentFilter => new()
    {
        Text = SearchText ?? "",
        Genre = SelectedGenre?.Value,
        Status = SelectedStatus?.Value ?? StatusFilter.All,
        MinRating = SelectedRating?.Value ?? 0,
    };

    public bool HasFilter => !CurrentFilter.IsEmpty;

    partial void OnSearchTextChanged(string value) => _ = ApplyFilterAfterDelayAsync();
    partial void OnSelectedGenreChanged(Option<Genre?> value) => ApplyFilterUnlessResetting();
    partial void OnSelectedRatingChanged(Option<int> value) => ApplyFilterUnlessResetting();
    partial void OnSelectedSortChanged(Option<SongSort> value) => ApplyFilterUnlessResetting();

    partial void OnSelectedStatusChanged(Option<StatusFilter> value)
    {
        foreach (var tab in StatusTabs)
            tab.IsSelected = tab.Value == value?.Value;
        ApplyFilterUnlessResetting();
    }

    private void ApplyFilterUnlessResetting()
    {
        if (!_resettingFilters)
            ApplyFilter();
    }

    /// <summary>Waits for a pause in typing so the list is not filtered on every key (RF-BUS-06).</summary>
    private async Task ApplyFilterAfterDelayAsync()
    {
        _searchDelay?.Cancel();
        var delay = _searchDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(SearchDelay, delay.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        var matches = CurrentFilter.ToPredicate();
        VisibleSongs = [.. Sort(_rows.Where(r => matches(r.Song)), SelectedSort?.Value ?? SongSort.Title)];
        BuildTileRows();
        OnPropertyChanged(nameof(HasFilter));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        UpdateCounts();
    }

    /// <summary>Title and artist ignore a leading "The"/"A", like the game; ties fall back to the title.</summary>
    private static IEnumerable<SongRowViewModel> Sort(IEnumerable<SongRowViewModel> rows, SongSort sort)
    {
        var text = StringComparer.CurrentCultureIgnoreCase;
        return sort switch
        {
            SongSort.Artist => rows.OrderBy(r => r.ArtistSortKey.Length == 0).ThenBy(r => r.ArtistSortKey, text).ThenBy(r => r.TitleSortKey, text),
            SongSort.Genre => rows.OrderBy(r => r.Genre.Length == 0).ThenBy(r => r.Genre, text).ThenBy(r => r.TitleSortKey, text),
            SongSort.Year => rows.OrderBy(r => r.Year is null).ThenBy(r => r.Year).ThenBy(r => r.TitleSortKey, text),
            SongSort.Bpm => rows.OrderBy(r => r.Bpm is null).ThenBy(r => r.Bpm).ThenBy(r => r.TitleSortKey, text),
            SongSort.Rating => rows.OrderByDescending(r => r.Rating).ThenBy(r => r.TitleSortKey, text),
            _ => rows.OrderBy(r => r.TitleSortKey, text),
        };
    }

    private void BuildTileRows()
    {
        var columns = Math.Max(1, Columns);
        TileRows = [.. VisibleSongs.Chunk(columns).Select(chunk => new TileRow(chunk, columns))];
    }

    private void UpdateCounts()
    {
        var total = _rows.Count;
        foreach (var tab in StatusTabs)
        {
            tab.Count = tab.Value switch
            {
                StatusFilter.Enabled => _rows.Count(r => r.IsEnabled),
                StatusFilter.Disabled => _rows.Count(r => !r.IsEnabled),
                StatusFilter.WithProblems => _rows.Count(r => r.HasProblem),
                _ => total,
            };
            if (tab.Value == StatusFilter.WithProblems)
                tab.IsVisible = tab.Count > 0 || tab.IsSelected;
        }
        CountText = VisibleSongs.Count == total ? Display.Songs(total) : $"{VisibleSongs.Count} of {Display.Songs(total)}";
        EmptyText =
            total == 0 ? (IsBusy ? null : "No songs yet. Use Import to add custom songs, or check the Fuser folder in Settings.")
            : VisibleSongs.Count == 0 ? "No songs match the search and filters."
            : null;
    }

    /// <summary>Shown over the grid when it has no tiles to show.</summary>
    [ObservableProperty]
    public partial string? EmptyText { get; private set; }

    [RelayCommand(CanExecute = nameof(HasFilter))]
    private void ClearFilters()
    {
        _resettingFilters = true;
        try
        {
            SearchText = "";
            SelectedGenre = GenreOptions[0];
            SelectedStatus = StatusOptions[0];
            SelectedRating = RatingOptions[0];
        }
        finally
        {
            _resettingFilters = false;
        }
        // Apply now instead of after the search delay.
        _searchDelay?.Cancel();
        ApplyFilter();
    }

    // ---- Selection and detail --------------------------------------------------------------

    public IReadOnlyList<SongRowViewModel> Rows => _rows;
    public IReadOnlyList<SongRowViewModel> Selection => _selection;

    [ObservableProperty]
    public partial SongDetailViewModel? Detail { get; private set; }

    /// <summary>
    /// A click on a tile: selects only that song, or with Ctrl adds or removes it, or with Shift selects the
    /// range from the last clicked song (several songs can be enabled or disabled at once).
    /// </summary>
    public void Click(SongRowViewModel row, bool control = false, bool shift = false)
    {
        if (shift && _anchor is not null)
        {
            var visible = VisibleSongs;
            int from = IndexOf(visible, _anchor), to = IndexOf(visible, row);
            if (from >= 0 && to >= 0)
            {
                var range = visible.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1);
                SetSelection(control ? _selection.Union(range) : range, keepAnchor: true);
                return;
            }
        }
        if (control)
        {
            SetSelection(_selection.Contains(row) ? _selection.Where(r => r != row) : _selection.Append(row), keepAnchor: true);
            _anchor = row;
            return;
        }
        SetSelection([row]);
    }

    /// <summary>Moves the selection by <paramref name="delta"/> tiles (arrows: ±1, ±a row). Returns the new index.</summary>
    public int MoveSelection(int delta)
    {
        var visible = VisibleSongs;
        if (visible.Count == 0)
            return -1;
        var current = _anchor is null ? -1 : IndexOf(visible, _anchor);
        var next = current < 0 ? 0 : Math.Clamp(current + delta, 0, visible.Count - 1);
        SetSelection([visible[next]]);
        return next;
    }

    private static int IndexOf(IReadOnlyList<SongRowViewModel> rows, SongRowViewModel row)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i] == row)
                return i;
        return -1;
    }

    public void SetSelection(IEnumerable<SongRowViewModel> rows) => SetSelection(rows, keepAnchor: false);

    private void SetSelection(IEnumerable<SongRowViewModel> rows, bool keepAnchor)
    {
        var selection = rows.Distinct().ToList();
        foreach (var row in _selection.Except(selection))
            row.IsSelected = false;
        foreach (var row in selection)
            row.IsSelected = true;
        _selection = selection;
        if (!keepAnchor)
            _anchor = selection.Count > 0 ? selection[^1] : null;

        var single = _selection.Count == 1 ? _selection[0] : null;
        if (Detail?.Row != single)
        {
            Detail?.Detach();
            Detail = single is null ? null : new SongDetailViewModel(single, _services, _dialogs, _shell);
            if (Detail?.Player is { } player && IsAutoplayOn)
                _ = player.PlayPauseCommand.ExecuteAsync(null);
        }
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        NotifyFileActionsChanged();
    }

    public string? SelectionText => _selection.Count > 1 ? $"{_selection.Count} songs selected" : null;

    public bool HasMultipleSelection => _selection.Count > 1;

    public bool HasNoSelection => _selection.Count == 0;

    /// <summary>Selects every song shown (Ctrl+A).</summary>
    public void SelectAllVisible() => SetSelection(VisibleSongs);

    /// <summary>Enter: enables the selected songs, or disables them when they are all enabled.</summary>
    [RelayCommand]
    private Task ToggleSelectedEnabled() =>
        _selection.Count == 0 ? Task.CompletedTask
        : _selection.All(r => r.IsEnabled) ? DisableSelectedCommand.ExecuteAsync(null)
        : EnableSelectedCommand.ExecuteAsync(null);

    // ---- Preview ---------------------------------------------------------------------------

    /// <summary>Plays the preview as soon as a song is selected (Z). Remembered between sessions.</summary>
    [ObservableProperty]
    public partial bool IsAutoplayOn { get; set; }

    partial void OnIsAutoplayOnChanged(bool value)
    {
        if (_services.Settings.AutoplayPreview == value)
            return;
        try
        {
            _services.SaveSettings(_services.Settings with { AutoplayPreview = value });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not save the autoplay setting");
        }
    }

    [RelayCommand]
    private void ToggleAutoplay() => IsAutoplayOn = !IsAutoplayOn;

    public const string ShortcutsHelp = """
        Z  Turn autoplay on or off: the song plays as soon as you select it
        Space  Play or pause the selected song
        Enter  Enable or disable the selected songs
        Arrow keys  Move between songs
        Ctrl+click, Shift+click  Select several songs
        Ctrl+A  Select every song shown
        Ctrl+F  Search
        Ctrl+I  Import songs
        F5  Rescan the library
        Esc  Clear the selection

        The preview loops like the game does: four phrases of eight bars. Click a disc to mute that part.
        Songs cannot be enabled, disabled or imported while Fuser is running.
        """;

    [RelayCommand]
    private Task ShowHelp() => _dialogs.ShowMessageAsync("Keyboard shortcuts", ShortcutsHelp);

    /// <summary>Space: plays or pauses the selected song.</summary>
    [RelayCommand]
    private Task PlayPause() =>
        Detail?.Player is { } player && player.PlayPauseCommand.CanExecute(null)
            ? player.PlayPauseCommand.ExecuteAsync(null)
            : Task.CompletedTask;

    // ---- Operation state -------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsGameRunning { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsProgressVisible { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial double ProgressMaximum { get; private set; } = 1;

    public bool CanChangeFiles => !IsBusy && !IsGameRunning && _services.IsConfigured;

    /// <summary>Why the file actions are unavailable, shown next to them (RF-EST-08).</summary>
    public string? FileActionsHint =>
        !_services.IsConfigured ? "Set the Fuser folder in Settings to manage songs."
        : IsGameRunning ? "Fuser is running. Close the game to enable, disable or import songs."
        : null;

    partial void OnIsBusyChanged(bool value)
    {
        NotifyFileActionsChanged();
        UpdateCounts();
    }
    partial void OnIsGameRunningChanged(bool value) => NotifyFileActionsChanged();

    private void NotifyFileActionsChanged()
    {
        OnPropertyChanged(nameof(CanChangeFiles));
        OnPropertyChanged(nameof(FileActionsHint));
        EnableSelectedCommand.NotifyCanExecuteChanged();
        DisableSelectedCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        ImportDisabledCommand.NotifyCanExecuteChanged();
        RescanCommand.NotifyCanExecuteChanged();
        FullRescanCommand.NotifyCanExecuteChanged();
        LaunchGameCommand.NotifyCanExecuteChanged();
        OpenSettingsCommand.NotifyCanExecuteChanged();
    }

    // ---- Startup ---------------------------------------------------------------------------

    /// <summary>Shows the saved library right away, then syncs it in the background (RF-BIB-02, 03).</summary>
    public async Task InitializeAsync()
    {
        _ = PollGameAsync(_lifetime.Token);
        await ReloadAsync();
        // Measured from process start, to track acceptance criterion 3 (list visible in under 1 s).
        Log.Information("Library shown: {Count} songs, {Elapsed:0} ms after start",
            _rows.Count, (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds);
        if (_services.IsConfigured)
        {
            await RunSyncAsync(force: false);
        }
        else
        {
            StatusText = MissingFolderMessage();
            Log.Warning("Library not scanned: {Reason}", StatusText);
        }
    }

    private async Task PollGameAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(GamePollInterval);
        do
        {
            try
            {
                var running = await Task.Run(_services.Game.IsGameRunning, CancellationToken.None);
                if (running != IsGameRunning)
                    Log.Information(running ? "Fuser is running: file actions locked" : "Fuser closed: file actions unlocked");
                IsGameRunning = running;
            }
            catch (Exception ex)
            {
                // Keep polling: one failed process query must not stop the checks.
                Log.Warning(ex, "Could not check whether Fuser is running");
            }
        }
        while (await WaitTickAsync(timer, ct));
    }

    private static async Task<bool> WaitTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Re-reads the songs from the database and updates the tiles in place, keeping the selection.</summary>
    public async Task ReloadAsync()
    {
        var songs = await _services.Repository.GetAllAsync();

        var current = songs.Select(s => s.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = _rows.Where(r => !current.Contains(r.FileName)).ToList();
        foreach (var row in removed)
        {
            _rows.Remove(row);
            _rowsByName.Remove(row.FileName);
        }

        foreach (var song in songs)
        {
            if (_rowsByName.TryGetValue(song.FileName, out var row))
            {
                row.Update(song);
            }
            else
            {
                row = new SongRowViewModel(song) { Loader = Thumbnails };
                _rowsByName.Add(song.FileName, row);
                _rows.Add(row);
            }
        }

        if (removed.Count > 0 && _selection.Intersect(removed).Any())
            SetSelection(_selection.Except(removed));
        OnPropertyChanged(nameof(Rows));
        // Rows updated in place may now match the filter differently (e.g. after enabling).
        ApplyFilter();
    }

    // ---- Scan ------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task Rescan() => RunSyncAsync(force: false);

    /// <summary>Reads every pak again, even unchanged ones (RF-BIB-09).</summary>
    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task FullRescan() => RunSyncAsync(force: true);

    private bool CanRescan() => !IsBusy && _services.IsConfigured;

    private async Task RunSyncAsync(bool force)
    {
        if (_services.Paths is not { } paths)
        {
            // Never sync against a missing folder: every song would look deleted.
            await _dialogs.ShowMessageAsync("Fuser folder not found", MissingFolderMessage());
            return;
        }

        await RunBusyAsync(force ? "Reading every song again…" : "Checking for changes…", async () =>
        {
            // Progress reports are posted to the UI thread and can arrive after the sync returns;
            // they must not overwrite the summary.
            var finished = false;
            var progress = new Progress<SyncProgress>(p =>
            {
                if (finished || p.Total == 0)
                    return;
                IsProgressIndeterminate = false;
                ProgressMaximum = p.Total;
                ProgressValue = p.Processed;
                StatusText = $"Reading songs: {p.Processed} of {p.Total}";
            });

            var options = new SyncOptions
            {
                ForceReread = force,
                Parallelism = _services.Settings.ScanParallelism ?? LibraryPaths.DefaultParallelism,
            };
            // Off the UI thread: otherwise every await inside the sync waits for the UI to be free (RF-UI-02).
            var result = await Task.Run(() => _services.Sync.SyncAsync(paths, options, progress, _lifetime.Token));
            finished = true;
            await ReloadAsync();
            StatusText = SyncSummary(result);
            LogSyncResult(result, paths, force);
        });
    }

    private void LogSyncResult(SyncResult result, LibraryPaths paths, bool force)
    {
        Log.Information("{Kind:l} in {Elapsed:0} ms: {Added} new, {Reread} re-read, {Moved} moved, {Removed} removed, {Unchanged} unchanged, {Problems} with problems",
            force ? "Full rescan" : "Sync", result.Elapsed.TotalMilliseconds, result.Added, result.Reread, result.Updated, result.Removed, result.Unchanged, result.ReadProblems.Count);
        foreach (var song in result.ReadProblems)
        {
            var path = song.RelativePath is null ? song.FileName : Path.Combine(paths.RootOf(song.Location), song.RelativePath);
            Log.Warning("{Status} read of {FileName} ({Path}): {Error}", song.ReadStatus, song.FileName, path, song.ReadError);
        }
        foreach (var duplicate in result.Duplicates)
            Log.Warning("Duplicate pak ignored: {Path} (a song with the same name is already in the library)", duplicate.FullPath);
    }

    private static string SyncSummary(SyncResult result)
    {
        var parts = new List<string>();
        if (result.Added > 0) parts.Add($"{result.Added} new");
        if (result.Reread > 0) parts.Add($"{result.Reread} updated");
        if (result.Updated > 0) parts.Add($"{result.Updated} moved");
        if (result.Removed > 0) parts.Add($"{result.Removed} removed");
        if (result.Failed > 0) parts.Add($"{result.Failed} could not be read");
        if (result.Duplicates.Count > 0) parts.Add($"{result.Duplicates.Count} duplicate paks ignored");

        var summary = parts.Count == 0 ? "Library is up to date" : "Library updated: " + string.Join(", ", parts);
        return $"{summary} ({result.Elapsed.TotalSeconds:0.0} s).";
    }

    // ---- Enable / disable ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanEnable))]
    private Task EnableSelected() => SetLocationAsync(SongLocation.Enabled);

    [RelayCommand(CanExecute = nameof(CanDisable))]
    private Task DisableSelected() => SetLocationAsync(SongLocation.Disabled);

    private bool CanEnable() => CanChangeFiles && _selection.Any(r => !r.IsEnabled);
    private bool CanDisable() => CanChangeFiles && _selection.Any(r => r.IsEnabled);

    private async Task SetLocationAsync(SongLocation target)
    {
        if (_services.Paths is not { } paths)
            return;

        var songs = _selection.Where(r => r.Song.Location != target).Select(r => r.Song).ToList();
        if (songs.Count == 0)
            return;

        var verb = target == SongLocation.Enabled ? "Enable" : "Disable";
        if (songs.Count > 1 && !await _dialogs.ConfirmAsync(
                $"{verb} songs",
                $"{verb} {songs.Count} songs? Their files will be moved to the {(target == SongLocation.Enabled ? "custom_songs" : "disabled songs")} folder.",
                verb))
            return;

        await RunBusyAsync($"{verb[..^1]}ing {Display.Songs(songs.Count)}…", async () =>
        {
            // File moves are synchronous: keep them off the UI thread so big batches do not freeze the window.
            var results = await Task.Run(() => _services.State.SetLocationAsync(songs, target, paths));
            await ReloadAsync();

            var moved = results.Count(r => r.Outcome == StateChangeOutcome.Moved);
            var failures = results.Where(r => !r.Succeeded).ToList();
            Log.Information("{Verb:l}d {Moved} of {Count} songs", verb, moved, songs.Count);
            foreach (var failure in failures)
                Log.Warning("Could not {Verb:l} {FileName}: {Outcome} {Message}", verb.ToLowerInvariant(), failure.FileName, failure.Outcome, failure.Message);
            StatusText = $"{verb}d {Display.Songs(moved)}." + (failures.Count > 0 ? $" {failures.Count} could not be changed." : "");

            if (failures.Count > 0)
            {
                var message = new StringBuilder();
                message.AppendLine(failures.Count == 1 ? "This song could not be changed:" : $"These {failures.Count} songs could not be changed:");
                foreach (var failure in failures)
                    message.AppendLine().Append("• ").Append(failure.FileName).Append(": ").Append(failure.Message ?? failure.Outcome.ToString());
                await _dialogs.ShowMessageAsync($"Could not {verb.ToLowerInvariant()} every song", message.ToString());
            }
        });
    }

    // ---- Import ----------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanChangeFiles))]
    private Task Import() => ImportAsync(SongLocation.Enabled);

    /// <summary>Adds the new songs straight to the disabled folder (RF-IMP-04).</summary>
    [RelayCommand(CanExecute = nameof(CanChangeFiles))]
    private Task ImportDisabled() => ImportAsync(SongLocation.Disabled);

    private async Task ImportAsync(SongLocation target)
    {
        if (_services.Paths is not { } paths)
            return;
        var sources = await _dialogs.PickImportSourcesAsync();
        if (sources.Count == 0)
            return;
        await ImportSourcesAsync(sources, target, paths);
    }

    public Task ImportSourcesAsync(IReadOnlyList<string> sources, SongLocation target, LibraryPaths paths) =>
        RunBusyAsync("Importing…", async () =>
        {
            var finished = false;
            var progress = new Progress<ImportProgress>(p =>
            {
                if (!finished)
                    StatusText = $"Importing {Path.GetFileName(p.Source)} ({p.SourceIndex + 1} of {p.SourceCount}): {p.Stage.ToLowerInvariant()}…";
            });

            var (import, _) = await Task.Run(() => _services.Import.ImportAsync(sources, paths, new ImportOptions { Target = target }, progress));
            finished = true;
            await ReloadAsync();

            var added = import.Imported.Count(s => !s.Replaced);
            var replaced = import.Imported.Count(s => s.Replaced);
            Log.Information("Import of {Sources}: {Added} added, {Replaced} replaced, {Issues} skipped", sources, added, replaced, import.Issues.Count);
            foreach (var song in import.Imported)
                Log.Information("Imported {FileName} into {Location} folder as {RelativePath}{Replaced:l}", song.FileName, song.Location, song.RelativePath, song.Replaced ? " (replaced)" : "");
            foreach (var issue in import.Issues)
                Log.Warning("Import skipped {FileName} from {Source}: {Kind} {Message}", issue.FileName, issue.Source, issue.Kind, issue.Message);
            StatusText = import.Imported.Count == 0
                ? "No songs were imported."
                : $"Imported {Display.Songs(import.Imported.Count)}" + (replaced > 0 ? $" ({replaced} replaced)." : ".");
            await _dialogs.ShowMessageAsync("Import finished", ImportSummary(added, replaced, target, import.Issues));
        });

    private static string ImportSummary(int added, int replaced, SongLocation target, IReadOnlyList<ImportIssue> issues)
    {
        var text = new StringBuilder();
        if (added + replaced == 0)
            text.AppendLine("No songs were imported.");
        if (added > 0)
            text.AppendLine($"Added {Display.Songs(added)}{(target == SongLocation.Disabled ? " as disabled" : "")}.");
        if (replaced > 0)
            text.AppendLine($"Replaced {Display.Songs(replaced)} that were already installed (they keep their state, rating and notes).");

        if (issues.Count > 0)
        {
            text.AppendLine().AppendLine(issues.Count == 1 ? "1 item was skipped:" : $"{issues.Count} items were skipped:");
            foreach (var issue in issues)
            {
                var source = string.IsNullOrEmpty(issue.Source) ? "" : Path.GetFileName(issue.Source);
                var subject = (source, issue.FileName) switch
                {
                    ("", null) => "",
                    ("", var name) => $"{name}: ",
                    (var s, null) => $"{s}: ",
                    (var s, var name) => $"{s} › {name}: ",
                };
                text.AppendLine().Append("• ").Append(subject).Append(issue.Message);
            }
        }
        return text.ToString().TrimEnd();
    }

    // ---- Game and settings -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanLaunchGame))]
    private async Task LaunchGame()
    {
        // The game takes over the audio; do not keep a preview playing over it.
        Detail?.Player?.Stop();
        var result = _services.Launcher.Launch(_services.Settings);
        Log.Information("Launch Fuser with {Launcher}: {Outcome} {Message}", _services.Settings.LauncherType, result.Outcome, result.Message);
        if (result.Outcome == LaunchOutcome.Started)
        {
            StatusText = "Starting Fuser…";
            IsGameRunning = true;   // until the next poll confirms it, keep file actions locked
            return;
        }
        await _dialogs.ShowMessageAsync("Could not start Fuser", result.Message ?? result.Outcome.ToString());
    }

    private bool CanLaunchGame() => !IsBusy && !IsGameRunning && _services.IsConfigured;

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private async Task OpenSettings()
    {
        var settings = new SettingsViewModel(_services.Settings, _dialogs)
        {
            LogsDirectory = _services.LogsDirectory,
            Shell = _shell,
        };
        if (!await _dialogs.ShowSettingsAsync(settings))
            return;

        var previous = _services.Paths;
        var saved = settings.ToSettings(_services.Settings);
        _services.SaveSettings(saved);
        Log.Information("Settings saved: Fuser folder {FuserFolder}, launcher {Launcher}, executable {Executable}",
            saved.FuserDirectory, saved.LauncherType, saved.ExecutablePath);
        NotifyFileActionsChanged();

        if (_services.Paths is { } paths && paths != previous)
            await RunSyncAsync(force: false);
        else if (_services.Paths is null)
            StatusText = MissingFolderMessage();
    }

    private bool CanOpenSettings() => !IsBusy;

    // ---- Helpers ---------------------------------------------------------------------------

    private Serilog.ILogger Log => _services.Log;

    private string MissingFolderMessage() =>
        string.IsNullOrWhiteSpace(_services.Settings.FuserDirectory)
            ? "The Fuser folder is not set. Open Settings to choose it."
            : $"The Fuser folder was not found: {_services.Settings.FuserDirectory}. Check that the drive is connected, or choose the folder again in Settings. The library was not scanned, so no song data was lost.";

    /// <summary>Runs one file operation at a time and reports any unexpected error clearly (RF-UI-02, RF-UI-06).</summary>
    private async Task RunBusyAsync(string status, Func<Task> operation)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        StatusText = status;
        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Closing the window; the sync already saved what it read.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Operation failed: {Operation}", status);
            StatusText = "The operation failed.";
            await _dialogs.ShowMessageAsync("Something went wrong", $"{ex.Message}\n\n({ex.GetType().Name})");
        }
        finally
        {
            IsProgressVisible = false;
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _searchDelay?.Cancel();
        Detail?.Detach();
    }
}
