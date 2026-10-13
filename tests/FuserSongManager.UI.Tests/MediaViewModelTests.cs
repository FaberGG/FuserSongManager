using Avalonia.Headless.XUnit;
using FuserSongManager.Core.Models;
using FuserSongManager.UI.ViewModels;
using SkiaSharp;
using static FuserSongManager.UI.Tests.TestLibrary;

namespace FuserSongManager.UI.Tests;

public sealed class MediaViewModelTests : IDisposable
{
    private readonly TestLibrary _library = new();

    public MediaViewModelTests()
    {
        _library.AddPak(SongLocation.Enabled, "art_P", Meta("With Art", "A") with { Mode = SongMode.Minor });
        _library.AddPak(SongLocation.Enabled, "plain_P", Meta("No Art", "B"));
        _library.Media.Art["art_P"] = FakeMedia.Png(SKColors.OrangeRed);
    }

    public void Dispose() => _library.Dispose();

    private async Task<(MainViewModel Vm, SongDetailViewModel Detail)> SelectAsync(string fileName)
    {
        var vm = _library.CreateMainViewModel();
        await vm.InitializeAsync();
        vm.SetSelection([vm.Rows.Single(r => r.FileName == fileName)]);
        var detail = vm.Detail!;
        await detail.ArtLoaded;
        return (vm, detail);
    }

    [AvaloniaFact]
    public async Task Album_art_is_shown_and_its_absence_explained()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using (vm)
        {
            Assert.NotNull(detail.AlbumArt);
            Assert.Equal(64, detail.AlbumArt!.PixelSize.Width);
            Assert.Null(detail.ArtMessage);

            vm.SetSelection([vm.Rows.Single(r => r.FileName == "plain_P")]);
            await vm.Detail!.ArtLoaded;
            Assert.Null(vm.Detail.AlbumArt);
            Assert.Equal("This song has no album art.", vm.Detail.ArtMessage);
        }
    }

    [AvaloniaFact]
    public async Task Album_art_is_cached_on_disk()
    {
        (await SelectAsync("art_P")).Vm.Dispose();
        _library.Media.Art.Clear();   // the pak would now say "no art"

        var (vm, detail) = await SelectAsync("art_P");
        using (vm)
            Assert.NotNull(detail.AlbumArt);
    }

    [AvaloniaFact]
    public async Task Play_reads_the_pak_once_in_the_songs_key_and_pause_resumes()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        Assert.Equal(PlayerState.Idle, player.State);
        Assert.Empty(_library.Media.AudioReads);   // nothing read until play is pressed

        await player.PlayPauseCommand.ExecuteAsync(null);
        Assert.Equal(PlayerState.Playing, player.State);
        Assert.True(_library.Player.IsPlaying);
        Assert.Equal(SongMode.Minor, Assert.Single(_library.Media.AudioReads).Mode);
        Assert.Equal(61, player.DurationSeconds);
        Assert.Equal("0:00 / 1:01", player.TimeText);

        await player.PlayPauseCommand.ExecuteAsync(null);
        Assert.Equal(PlayerState.Paused, player.State);
        await player.PlayPauseCommand.ExecuteAsync(null);
        Assert.Equal(PlayerState.Playing, player.State);
        Assert.Single(_library.Media.AudioReads);
    }

    [AvaloniaFact]
    public async Task Parts_can_be_turned_off_and_missing_parts_are_disabled()
    {
        _library.Media.Stems.Remove(StemKind.Bass);
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        player.Stems.Single(s => s.Kind == StemKind.Lead).IsOn = false;   // before playing: applied on load

        await player.PlayPauseCommand.ExecuteAsync(null);
        player.Stems.Single(s => s.Kind == StemKind.Beat).IsOn = false;

        Assert.Equal([StemKind.Beat, StemKind.Lead], _library.Player.Muted.Order());
        var bass = player.Stems.Single(s => s.Kind == StemKind.Bass);
        Assert.False(bass.IsAvailable);
        Assert.Contains("no bass part", bass.ToolTip);

        player.Stems.Single(s => s.Kind == StemKind.Beat).IsOn = true;
        Assert.DoesNotContain(StemKind.Beat, _library.Player.Muted);
    }

    [AvaloniaFact]
    public async Task Seeking_stop_and_the_end_of_the_song()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        await player.PlayPauseCommand.ExecuteAsync(null);

        player.PositionSeconds = 30;
        Assert.Equal(TimeSpan.FromSeconds(30), _library.Player.Position);

        player.Stop();
        Assert.Equal((PlayerState.Paused, 0d), (player.State, player.PositionSeconds));

        await player.PlayPauseCommand.ExecuteAsync(null);
        _library.Player.End();
        await Task.Delay(500);   // noticed by the playback clock
        Assert.Equal(PlayerState.Paused, player.State);
        Assert.Equal(0, player.PositionSeconds);
    }

    [AvaloniaFact]
    public async Task Unplayable_audio_is_explained()
    {
        _library.Media.AudioError = new NotSupportedException("This song's audio uses MOGG encryption version 0x10, which cannot be played.");
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;

        await player.PlayPauseCommand.ExecuteAsync(null);

        Assert.Equal(PlayerState.Unavailable, player.State);
        Assert.Contains("0x10", player.Message);
        Assert.False(player.PlayPauseCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Selecting_another_song_or_launching_the_game_stops_the_preview()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        await detail.Player!.PlayPauseCommand.ExecuteAsync(null);

        await vm.LaunchGameCommand.ExecuteAsync(null);
        Assert.False(_library.Player.IsPlaying);

        vm.SetSelection([vm.Rows.Single(r => r.FileName == "plain_P")]);
        Assert.True(_library.Player.Disposed);
    }
}
