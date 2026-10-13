using System.Diagnostics;
using Avalonia.Collections;
using Avalonia.Headless.XUnit;
using FuserSongManager.Core.Models;
using FuserSongManager.UI.ViewModels;
using static FuserSongManager.UI.Tests.TestLibrary;

namespace FuserSongManager.UI.Tests;

/// <summary>MVP acceptance criterion 8: search and sort feel instant with at least 2000 songs.</summary>
public sealed class PerformanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestLibrary _library = new();

    public void Dispose() => _library.Dispose();

    [AvaloniaFact]
    public async Task Search_and_sort_stay_fast_with_2000_songs()
    {
        var genres = Enum.GetValues<Genre>();
        for (var i = 0; i < 2000; i++)
            _library.AddPak(i % 3 == 0 ? SongLocation.Disabled : SongLocation.Enabled, $"song{i:0000}_P",
                Meta($"{(i % 4 == 0 ? "The " : "")}Title {i}", $"Artist {i % 150}", genres[i % genres.Length], 1960 + i % 60, 80 + i % 90));

        using var vm = _library.CreateMainViewModel();
        await vm.InitializeAsync();
        Assert.Equal(2000, vm.Rows.Count);

        var watch = Stopwatch.StartNew();
        vm.SearchText = "artist 12";
        vm.ApplyFilter();   // what the search delay ends up calling
        var search = watch.Elapsed;

        watch.Restart();
        vm.SelectedGenre = vm.GenreOptions.Single(o => o.Value == Genre.Rock);
        var filter = watch.Elapsed;

        watch.Restart();
        vm.ClearFiltersCommand.Execute(null);
        vm.Songs.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(SongRowViewModel.TitleSortKey)));
        _ = vm.Songs.Count;
        var sort = watch.Elapsed;

        output.WriteLine($"search {search.TotalMilliseconds:0} ms, filter {filter.TotalMilliseconds:0} ms, sort {sort.TotalMilliseconds:0} ms");
        Assert.Equal("Title 0", ((SongRowViewModel)vm.Songs[0]!).Title.Replace("The ", ""));
        Assert.True(search < TimeSpan.FromMilliseconds(200), $"search took {search.TotalMilliseconds} ms");
        Assert.True(filter < TimeSpan.FromMilliseconds(200), $"filter took {filter.TotalMilliseconds} ms");
        Assert.True(sort < TimeSpan.FromMilliseconds(300), $"sort took {sort.TotalMilliseconds} ms");
    }
}
