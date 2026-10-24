using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using FuserSongManager.Core.Models;
using FuserSongManager.UI.Controls;
using FuserSongManager.UI.ViewModels;
using SkiaSharp;
using static FuserSongManager.UI.Tests.TestLibrary;

namespace FuserSongManager.UI.Tests;

/// <summary>The crate-style library: tiles, selection, autoplay, the looping preview and the hover sweep.</summary>
public sealed class LibraryScreenTests : IDisposable
{
    private readonly TestLibrary _library = new();

    public LibraryScreenTests()
    {
        _library.AddPak(SongLocation.Enabled, "a_P", Meta("Alpha", "One", bpm: 120));
        _library.AddPak(SongLocation.Enabled, "b_P", Meta("Bravo", "Two"));
        _library.AddPak(SongLocation.Disabled, "c_P", Meta("Charlie", "Three"));
        _library.AddPak(SongLocation.Enabled, "d_P", Meta("Delta", "Four"));
        _library.Media.Art["a_P"] = FakeMedia.Png(SKColors.MediumPurple);
    }

    public void Dispose() => _library.Dispose();

    private async Task<MainViewModel> StartAsync()
    {
        var vm = _library.CreateMainViewModel();
        await vm.InitializeAsync();
        return vm;
    }

    private static SongRowViewModel Row(MainViewModel vm, string fileName) => vm.Rows.Single(r => r.FileName == fileName);

    private static List<string> Selected(MainViewModel vm) => [.. vm.Selection.Select(r => r.FileName).Order()];

    [AvaloniaFact]
    public async Task Clicks_select_one_song_or_several_with_ctrl_and_shift()
    {
        using var vm = await StartAsync();

        vm.Click(Row(vm, "a_P"));
        Assert.Equal(["a_P"], Selected(vm));
        Assert.True(Row(vm, "a_P").IsSelected);
        Assert.NotNull(vm.Detail);

        vm.Click(Row(vm, "c_P"), shift: true);
        Assert.Equal(["a_P", "b_P", "c_P"], Selected(vm));
        Assert.True(vm.HasMultipleSelection);
        Assert.Null(vm.Detail);

        vm.Click(Row(vm, "b_P"), control: true);
        Assert.Equal(["a_P", "c_P"], Selected(vm));
        Assert.False(Row(vm, "b_P").IsSelected);

        vm.Click(Row(vm, "d_P"));
        Assert.Equal(["d_P"], Selected(vm));
        Assert.False(Row(vm, "a_P").IsSelected);
    }

    [AvaloniaFact]
    public async Task Arrows_move_by_one_tile_or_by_a_row()
    {
        using var vm = await StartAsync();
        vm.Columns = 2;
        Assert.Equal([2, 2], vm.TileRows.Select(r => r.Songs.Count));

        Assert.Equal(0, vm.MoveSelection(1));   // nothing selected yet: starts at the first tile
        Assert.Equal(2, vm.MoveSelection(vm.Columns));
        Assert.Equal("c_P", vm.Detail?.FileName);
        Assert.Equal(3, vm.MoveSelection(5));   // stops at the last tile
    }

    [AvaloniaFact]
    public async Task Enter_enables_or_disables_the_selection()
    {
        using var vm = await StartAsync();

        vm.Click(Row(vm, "a_P"));
        await vm.ToggleSelectedEnabledCommand.ExecuteAsync(null);
        Assert.False(Row(vm, "a_P").IsEnabled);
        Assert.True(File.Exists(Path.Combine(_library.Paths.DisabledDirectory, "a_P.pak")));

        // A mix of enabled and disabled songs gets enabled.
        vm.Click(Row(vm, "b_P"), control: true);
        await vm.ToggleSelectedEnabledCommand.ExecuteAsync(null);
        Assert.True(Row(vm, "a_P").IsEnabled);
        Assert.True(Row(vm, "b_P").IsEnabled);
    }

    [AvaloniaFact]
    public async Task Autoplay_plays_each_song_as_it_is_selected_and_is_remembered()
    {
        using (var vm = await StartAsync())
        {
            vm.Click(Row(vm, "a_P"));
            Assert.Equal(PlayerState.Idle, vm.Detail!.Player!.State);   // off by default

            vm.ToggleAutoplayCommand.Execute(null);
            vm.Click(Row(vm, "b_P"));
            await WaitFor(() => vm.Detail!.Player!.State == PlayerState.Playing);
            Assert.True(_library.Player.Loop);
        }

        Assert.True(_library.Settings.Current.AutoplayPreview);
        using var again = await StartAsync();
        Assert.True(again.IsAutoplayOn);
    }

    [AvaloniaFact]
    public async Task Preview_counts_bars_and_phrases_and_spins_the_discs_that_play()
    {
        using var vm = await StartAsync();
        vm.Click(Row(vm, "a_P"));   // 120 BPM: a bar lasts 2 s; the fake song lasts 61 s
        var player = vm.Detail!.Player!;
        Assert.Equal("Plays in a loop, like in the game", player.BarText);
        Assert.Equal(["Drums", "Bass guitar", "Synth", "Vocals"], player.Stems.Select(s => s.InstrumentName));

        await player.PlayPauseCommand.ExecuteAsync(null);
        player.PositionSeconds = 17;

        Assert.Equal(30, player.BarCount);   // 30.5 bars, rounded
        Assert.Equal(9, player.CurrentBar);
        Assert.Equal(2, player.CurrentPhrase);
        Assert.Equal("Bar 9 of 30 · Phrase 2 of 4", player.BarText);
        Assert.All(player.Stems, s => Assert.True(s.IsSpinning));

        player.Stems[0].IsOn = false;
        Assert.False(player.Stems[0].IsSpinning);
        await player.PlayPauseCommand.ExecuteAsync(null);   // pause
        Assert.DoesNotContain(player.Stems, s => s.IsSpinning);
    }

    [AvaloniaFact]
    public async Task Tiles_load_their_album_art_and_show_the_parts()
    {
        using var vm = await StartAsync();
        foreach (var row in vm.Rows)
            row.RequestThumbnail();
        await vm.Thumbnails.Idle;

        Assert.NotNull(Row(vm, "a_P").Thumbnail);
        Assert.False(Row(vm, "a_P").ShowPlaceholder);
        Assert.Null(Row(vm, "b_P").Thumbnail);
        Assert.True(Row(vm, "b_P").ShowPlaceholder);
        Assert.Equal(ThumbnailLoaderWidth, Row(vm, "a_P").Thumbnail!.PixelSize.Width);
        Assert.Equal([StemKind.Beat, StemKind.Bass, StemKind.Loop, StemKind.Lead], Row(vm, "a_P").Parts.Select(p => p.Kind));
        Assert.Equal("Rock · 2000", Row(vm, "a_P").GenreAndYear);
    }

    // The fake art is 64 px, so decoding "to width" scales it up to the tile width.
    private const int ThumbnailLoaderWidth = Services.ThumbnailLoader.Width;

    [Theory]
    [InlineData(StemKind.Bass, "Guitar", "Bass guitar", "Icon.Guitar")]
    [InlineData(StemKind.Loop, "Guitar", "Guitar", "Icon.Guitar")]
    [InlineData(StemKind.Loop, "Sampler", "Sampler", "Icon.Sampler")]
    [InlineData(StemKind.Bass, "Synth", "Synth", "Icon.Synth")]
    [InlineData(StemKind.Loop, "Horns", "Horns", "Icon.Horns")]
    [InlineData(StemKind.Lead, "Vocal", "Vocals", "Icon.Vocals")]
    [InlineData(StemKind.Bass, "None", "No instrument", null)]
    [InlineData(StemKind.Beat, null, "No instrument", null)]
    public void Instruments_are_named_and_drawn_like_the_game(StemKind part, string? instrument, string name, string? icon)
    {
        Assert.Equal(name, Display.Instrument(part, instrument));
        Assert.Equal(icon, Display.InstrumentIconKey(instrument));
    }

    [AvaloniaFact]
    public void Every_instrument_icon_exists()
    {
        foreach (var key in new[] { "Icon.Drums", "Icon.Guitar", "Icon.Synth", "Icon.Sampler", "Icon.Horns", "Icon.Strings", "Icon.Vocals" })
            Assert.IsAssignableFrom<Geometry>(Converters.IconGeometry.Convert(key, typeof(Geometry), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public async Task Hover_colour_appears_at_once_and_sweeps_away_when_the_pointer_leaves()
    {
        var sweep = new SweepFill { Fill = Brushes.Violet, Duration = TimeSpan.FromMilliseconds(400), Width = 200, Height = 40 };
        var window = new Window { Content = sweep, Width = 200, Height = 40 };
        window.Show();

        sweep.IsActive = true;
        Assert.Equal(0, sweep.Progress);   // no fade-in: the colour is there right away

        // The animation runs on the render clock, which follows real time.
        sweep.IsActive = false;
        await Frame();
        await Task.Delay(150);
        await Frame();
        Assert.InRange(sweep.Progress, 0.0001, 0.9999);

        await Task.Delay(400);
        await Frame();
        Assert.Equal(1, sweep.Progress);

        // Hovering again brings it back in full, even mid-sweep.
        sweep.IsActive = true;
        Assert.Equal(0, sweep.Progress);
        window.Close();
    }

    private static async Task Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < timeout, "Timed out.");
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
