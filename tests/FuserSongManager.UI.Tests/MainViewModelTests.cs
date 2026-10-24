using System.ComponentModel;
using System.IO.Compression;
using Avalonia.Headless.XUnit;
using FuserSongManager.Core.Models;
using FuserSongManager.UI.ViewModels;
using static FuserSongManager.UI.Tests.TestLibrary;

namespace FuserSongManager.UI.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly TestLibrary _library = new();

    public MainViewModelTests()
    {
        _library.AddPak(SongLocation.Enabled, "brightside_P", Meta("Mr. Brightside", "The Killers", Genre.Rock, 2003));
        _library.AddPak(SongLocation.Enabled, "africa_P", Meta("Africa", "Toto", Genre.Pop, 1982), subfolder: "Pack");
        _library.AddPak(SongLocation.Disabled, "around_P", Meta("Around the World", "Daft Punk", Genre.Dance, 1997));
        _library.AddPak(SongLocation.Disabled, "zombie_P", Meta("A Zombie Song", "Band", Genre.Rock, 1994));
    }

    public void Dispose() => _library.Dispose();

    private async Task<MainViewModel> StartAsync()
    {
        var viewModel = _library.CreateMainViewModel();
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static SongRowViewModel Row(MainViewModel vm, string fileName) => vm.Rows.Single(r => r.FileName == fileName);

    private static List<string> VisibleTitles(MainViewModel vm) => vm.VisibleSongs.Select(r => r.Title).ToList();

    [AvaloniaFact]
    public async Task Startup_scans_the_library_and_shows_every_song()
    {
        using var vm = await StartAsync();

        Assert.Equal(4, vm.Rows.Count);
        Assert.Equal("4 songs", vm.CountText);
        Assert.StartsWith("Library updated: 4 new", vm.StatusText);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.EmptyText);
        Assert.Equal("Disabled", Row(vm, "around_P").State);
    }

    [AvaloniaFact]
    public async Task Second_startup_shows_the_saved_library_and_reads_nothing()
    {
        (await StartAsync()).Dispose();
        _library.Reader.Metadata.Clear();   // any read would now fail

        using var vm = await StartAsync();

        Assert.Equal(4, vm.Rows.Count);
        Assert.StartsWith("Library is up to date", vm.StatusText);
        Assert.Equal("Mr. Brightside", Row(vm, "brightside_P").Title);
    }

    [AvaloniaFact]
    public async Task Search_filters_after_a_short_pause_and_shows_the_count()
    {
        using var vm = await StartAsync();

        vm.SearchText = "killers";
        Assert.Equal(4, vm.VisibleSongs.Count);   // not yet: waits for the user to stop typing
        await Task.Delay(MainViewModel.SearchDelay * 3);

        Assert.Equal(["Mr. Brightside"], VisibleTitles(vm));
        Assert.Equal("1 of 4 songs", vm.CountText);

        vm.SearchText = "nothing like this";
        await Task.Delay(MainViewModel.SearchDelay * 3);
        Assert.Equal("No songs match the search and filters.", vm.EmptyText);
    }

    [AvaloniaFact]
    public async Task Filters_combine_and_clear_with_one_action()
    {
        using var vm = await StartAsync();

        vm.SelectedGenre = vm.GenreOptions.Single(o => o.Value == Genre.Rock);
        Assert.Equal(2, vm.VisibleSongs.Count);
        vm.SelectedStatus = vm.StatusOptions.Single(o => o.Label == "Disabled");
        Assert.Equal(["A Zombie Song"], VisibleTitles(vm));
        Assert.True(vm.ClearFiltersCommand.CanExecute(null));

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal(4, vm.VisibleSongs.Count);
        Assert.Equal("4 songs", vm.CountText);
        Assert.False(vm.ClearFiltersCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Sorting_by_title_ignores_the_and_a()
    {
        using var vm = await StartAsync();

        // Title is the default order. "A Zombie Song" sorts as "Zombie Song".
        Assert.Equal(["Africa", "Around the World", "Mr. Brightside", "A Zombie Song"], VisibleTitles(vm));

        vm.SelectedSort = vm.SortOptions.Single(o => o.Value == SongSort.Year);
        var years = vm.VisibleSongs.Select(r => r.Year ?? int.MaxValue).ToList();
        Assert.Equal(years.Order(), years);
        // The grid shows the same order, cut into rows.
        Assert.Equal(VisibleTitles(vm), vm.TileRows.SelectMany(r => r.Songs).Select(s => s.Title).ToList());
    }

    [AvaloniaFact]
    public async Task Enabling_several_songs_asks_first_then_moves_their_files()
    {
        using var vm = await StartAsync();
        vm.SetSelection([Row(vm, "around_P"), Row(vm, "zombie_P"), Row(vm, "africa_P")]);
        Assert.True(vm.EnableSelectedCommand.CanExecute(null));

        await vm.EnableSelectedCommand.ExecuteAsync(null);

        Assert.Contains("Enable 2 songs?", Assert.Single(_library.Dialogs.Confirmations));   // the enabled one is not counted
        Assert.True(File.Exists(Path.Combine(_library.Paths.EnabledDirectory, "around_P.pak")));
        Assert.True(File.Exists(Path.Combine(_library.Paths.EnabledDirectory, "zombie_P.sig")));
        Assert.Equal("Enabled", Row(vm, "around_P").State);
        Assert.Equal("Enabled 2 songs.", vm.StatusText);
        Assert.Empty(_library.Dialogs.Messages);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_confirmation_moves_nothing()
    {
        using var vm = await StartAsync();
        _library.Dialogs.ConfirmAnswer = false;
        vm.SetSelection([Row(vm, "brightside_P"), Row(vm, "africa_P")]);

        await vm.DisableSelectedCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(_library.Paths.EnabledDirectory, "brightside_P.pak")));
        Assert.Equal("Enabled", Row(vm, "brightside_P").State);
    }

    [AvaloniaFact]
    public async Task One_song_is_disabled_without_asking_and_keeps_its_subfolder()
    {
        using var vm = await StartAsync();
        vm.SetSelection([Row(vm, "africa_P")]);

        await vm.DisableSelectedCommand.ExecuteAsync(null);

        Assert.Empty(_library.Dialogs.Confirmations);
        Assert.True(File.Exists(Path.Combine(_library.Paths.DisabledDirectory, "Pack", "africa_P.pak")));
        Assert.False(vm.DisableSelectedCommand.CanExecute(null));
        Assert.True(vm.EnableSelectedCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Songs_that_cannot_be_enabled_are_explained()
    {
        _library.AddPak(SongLocation.Disabled, "nosig_P", Meta("No Sig", "X"), withSig: false);
        using var vm = await StartAsync();
        vm.SetSelection([Row(vm, "nosig_P")]);

        await vm.EnableSelectedCommand.ExecuteAsync(null);

        var (title, message) = Assert.Single(_library.Dialogs.Messages);
        Assert.Equal("Could not enable every song", title);
        Assert.Contains("nosig_P", message);
        Assert.Contains("nosig_P.sig", message);
        Assert.True(Row(vm, "nosig_P").HasProblem);
    }

    [AvaloniaFact]
    public async Task File_actions_are_locked_while_the_game_runs()
    {
        _library.Game.Running = true;
        using var vm = await StartAsync();
        await Task.Delay(100);   // first game check runs in the background
        vm.SetSelection([Row(vm, "around_P")]);

        Assert.True(vm.IsGameRunning);
        Assert.False(vm.CanChangeFiles);
        Assert.False(vm.EnableSelectedCommand.CanExecute(null));
        Assert.False(vm.ImportCommand.CanExecute(null));
        Assert.False(vm.LaunchGameCommand.CanExecute(null));
        Assert.True(vm.RescanCommand.CanExecute(null));
        Assert.Contains("Close the game", vm.FileActionsHint);
    }

    [AvaloniaFact]
    public async Task Import_adds_the_songs_and_reports_what_was_skipped()
    {
        using var vm = await StartAsync();
        var zip = Path.Combine(_library.Root, "new songs.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (entry, content) in new[] { ("Pack/new_P.pak", "pak"), ("Pack/new_P.sig", "sig"), ("Pack/broken_P.pak", "pak") })
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
                writer.Write(content);
            }
        }
        _library.Reader.Metadata["new_P"] = Meta("Brand New", "Someone");
        _library.Dialogs.ImportSources = [zip];

        await vm.ImportDisabledCommand.ExecuteAsync(null);

        Assert.Equal("Disabled", Row(vm, "new_P").State);
        Assert.Equal("Brand New", Row(vm, "new_P").Title);
        Assert.Equal(5, vm.Rows.Count);
        var (title, message) = Assert.Single(_library.Dialogs.Messages);
        Assert.Equal("Import finished", title);
        Assert.Contains("Added 1 song as disabled.", message);
        Assert.Contains("new songs.zip › broken_P:", message);
    }

    [AvaloniaFact]
    public async Task Detail_edits_are_saved_and_shown_in_the_list()
    {
        using var vm = await StartAsync();
        vm.SetSelection([Row(vm, "africa_P")]);
        var detail = Assert.IsType<SongDetailViewModel>(vm.Detail);

        detail.SetRatingCommand.Execute(4);
        await detail.PendingSave;
        detail.Notes = "Great opener";
        await detail.PendingSave;
        detail.Author = "  PakMaker ";
        await detail.PendingSave;

        var saved = (await _library.Repository.GetAllAsync()).Single(s => s.FileName == "africa_P");
        Assert.Equal((4, "Great opener", "PakMaker"), (saved.Rating, saved.Notes, saved.Author));
        Assert.Equal("★★★★☆", Row(vm, "africa_P").Stars);
        Assert.Equal("PakMaker", Row(vm, "africa_P").Author);

        detail.SetRatingCommand.Execute(4);   // same star again clears it
        await detail.PendingSave;
        Assert.Equal(0, Row(vm, "africa_P").Rating);
    }

    [AvaloniaFact]
    public async Task Rescan_keeps_user_data_and_does_not_disturb_text_being_typed()
    {
        using var vm = await StartAsync();
        vm.SetSelection([Row(vm, "brightside_P")]);
        var detail = vm.Detail!;
        detail.SetRatingCommand.Execute(5);
        await detail.PendingSave;
        var raised = new List<string?>();
        ((INotifyPropertyChanged)detail).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await vm.FullRescanCommand.ExecuteAsync(null);

        Assert.Equal(5, Row(vm, "brightside_P").Rating);
        Assert.DoesNotContain(nameof(SongDetailViewModel.Notes), raised);
        Assert.DoesNotContain(nameof(SongDetailViewModel.Author), raised);
    }

    [AvaloniaFact]
    public async Task Show_in_folder_opens_the_pak_location()
    {
        using var vm = await StartAsync();
        vm.SetSelection([Row(vm, "africa_P")]);

        vm.Detail!.OpenFolderCommand.Execute(null);

        Assert.Equal(Path.Combine(_library.Paths.EnabledDirectory, "Pack", "africa_P.pak"), Assert.Single(_library.Shell.Shown));
    }

    [AvaloniaFact]
    public async Task Missing_fuser_folder_keeps_the_library_instead_of_emptying_it()
    {
        (await StartAsync()).Dispose();
        _library.Settings.Save(new AppSettings { FuserDirectory = Path.Combine(_library.Root, "unplugged drive") });

        using var vm = await StartAsync();

        Assert.Equal(4, vm.Rows.Count);
        Assert.Equal(4, (await _library.Repository.GetAllAsync()).Count);
        Assert.Contains("was not found", vm.StatusText);
        Assert.False(vm.RescanCommand.CanExecute(null));
        Assert.Contains("Settings", vm.FileActionsHint);
    }

    [AvaloniaFact]
    public async Task Launch_starts_the_game_and_locks_file_actions()
    {
        using var vm = await StartAsync();

        await vm.LaunchGameCommand.ExecuteAsync(null);

        Assert.Equal(1, _library.Game.Launches);
        Assert.False(vm.CanChangeFiles);
    }

    [AvaloniaFact]
    public async Task Changing_the_fuser_folder_in_settings_rescans_the_new_library()
    {
        using var vm = await StartAsync();
        var other = Path.Combine(_library.Root, "Other Fuser");
        var otherPaths = LibraryPaths.FromFuserDirectory(other);
        Directory.CreateDirectory(otherPaths.EnabledDirectory);
        File.WriteAllText(Path.Combine(otherPaths.EnabledDirectory, "solo_P.pak"), "pak");
        File.WriteAllText(Path.Combine(otherPaths.EnabledDirectory, "solo_P.sig"), "sig");
        _library.Reader.Metadata["solo_P"] = Meta("Solo", "One");
        _library.Dialogs.OnSettings = settings =>
        {
            settings.FuserDirectory = otherPaths.EnabledDirectory;   // a folder inside the install is accepted
            return settings.CanSave;
        };

        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(other, _library.Settings.Current.FuserDirectory);
        Assert.True(Directory.Exists(otherPaths.DisabledDirectory));   // created on save
        Assert.Equal(["Solo"], VisibleTitles(vm));
    }
}
