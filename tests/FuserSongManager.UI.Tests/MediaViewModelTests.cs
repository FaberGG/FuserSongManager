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
    public async Task Riser_while_the_song_plays_goes_through_the_same_player_for_the_next_bar()
    {
        var (vm, detail) = await SelectAsync("art_P");   // 120 BPM in A minor: a bar is two seconds
        using var _ = vm;
        var player = detail.Player!;
        var riser = player.Riser!;
        Assert.Equal(RiserState.Idle, riser.State);
        Assert.Empty(_library.Media.RiserReads);   // nothing read until the button is pressed

        await player.PlayPauseCommand.ExecuteAsync(null);
        await riser.PlayCommand.ExecuteAsync(null);

        Assert.Equal(RiserState.Playing, riser.State);
        // The song is not paused: the player waits for the bar and fades the song itself.
        Assert.True(_library.Player.SongWasPlayingAtRiser);
        Assert.True(_library.Player.IsPlaying);
        Assert.Equal(PlayerState.Playing, player.State);
        Assert.Equal(TimeSpan.FromSeconds(2), _library.Player.RiserBar);
        Assert.Equal(SongMode.Minor, Assert.Single(_library.Media.RiserReads).Mode);
        Assert.All(_library.Player.Riser!.Stems, s => Assert.Equal("riser", s.ZoneLabel));
        Assert.Single(_library.Media.AudioReads);   // the song was already loaded
    }

    [AvaloniaFact]
    public async Task The_riser_effects_are_named_as_djs_call_them_and_the_choice_is_what_plays_and_is_remembered()
    {
        var (vm, detail) = await SelectAsync("art_P");
        var riser = detail.Player!.Riser!;
        Assert.Equal(
            ["Fade Out", "Low Pass Filter", "Echo Out", "Dub Echo", "Trance Gate", "Strip Down", "Rhythm Hold"],
            riser.Effects.Select(e => e.Label));
        Assert.Equal(RiserEffect.LowPassFilter, riser.SelectedEffect.Value);   // the default

        riser.SelectedEffect = riser.Effects.Single(e => e.Label == "Trance Gate");
        await detail.Player.PlayPauseCommand.ExecuteAsync(null);
        await riser.PlayCommand.ExecuteAsync(null);

        Assert.Equal(RiserEffect.TranceGate, _library.Player.RiserEffectUsed);
        Assert.Equal(RiserEffect.TranceGate, _library.Settings.Current.RiserEffect);
        vm.Dispose();

        // Another song, another session: it starts with the same effect.
        var (again, other) = await SelectAsync("plain_P");
        using var _ = again;
        Assert.Equal("Trance Gate", other.Player!.Riser!.SelectedEffect.Label);
    }

    [AvaloniaFact]
    public async Task Riser_before_the_song_was_played_loads_it_and_leads_into_it()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        Assert.Empty(_library.Media.AudioReads);

        await player.Riser!.PlayCommand.ExecuteAsync(null);

        Assert.Single(_library.Media.AudioReads);   // the song's audio had to be read for the riser to lead into it
        Assert.NotNull(_library.Player.Loaded);
        Assert.False(_library.Player.SongWasPlayingAtRiser);   // so the riser is heard alone first
        Assert.True(_library.Player.IsPlaying);
        Assert.Equal((PlayerState.Playing, RiserState.Playing), (player.State, player.Riser.State));
        Assert.True(_library.Player.Loop);
    }

    [AvaloniaFact]
    public async Task Riser_goes_back_to_ready_when_the_song_takes_over()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        var riser = player.Riser!;
        await player.PlayPauseCommand.ExecuteAsync(null);
        await riser.PlayCommand.ExecuteAsync(null);

        _library.Player.FinishRiser();
        await Task.Delay(400);   // noticed by the riser's clock

        Assert.Equal(RiserState.Idle, riser.State);
        Assert.Equal(PlayerState.Playing, player.State);   // the song is what plays now
        Assert.True(riser.PlayCommand.CanExecute(null));

        await riser.PlayCommand.ExecuteAsync(null);   // and it can be heard again, without reading the pak again
        Assert.Equal(RiserState.Playing, riser.State);
        Assert.Single(_library.Media.RiserReads);
    }

    [AvaloniaFact]
    public async Task The_riser_runs_with_discs_muted_and_the_song_comes_back_with_all_of_them_on()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        await player.PlayPauseCommand.ExecuteAsync(null);
        player.Stems.Single(s => s.Kind == StemKind.Beat).IsOn = false;
        player.Stems.Single(s => s.Kind == StemKind.Lead).IsOn = false;

        await player.Riser!.PlayCommand.ExecuteAsync(null);
        Assert.Equal([StemKind.Beat, StemKind.Lead], _library.Player.Muted.Order());   // the riser does not touch them
        Assert.Equal(2, player.Stems.Count(s => !s.IsOn));

        _library.Player.FinishRiser();   // the song starts again
        await Task.Delay(500);   // the discs follow on the next tick

        Assert.All(player.Stems, s => Assert.True(s.IsOn, $"{s.Name} is still off"));
        Assert.Empty(_library.Player.Muted);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_riser_leaves_the_discs_as_they_were()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        await player.PlayPauseCommand.ExecuteAsync(null);
        player.Stems.Single(s => s.Kind == StemKind.Bass).IsOn = false;
        await player.Riser!.PlayCommand.ExecuteAsync(null);

        await player.Riser.PlayCommand.ExecuteAsync(null);   // cancelled before it sounded
        await Task.Delay(500);

        Assert.False(player.Stems.Single(s => s.Kind == StemKind.Bass).IsOn);
        Assert.Equal([StemKind.Bass], _library.Player.Muted);
    }

    [AvaloniaFact]
    public async Task Pressing_the_riser_again_cancels_it_and_stopping_or_seeking_the_song_does_too()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        var riser = player.Riser!;
        await player.PlayPauseCommand.ExecuteAsync(null);

        await riser.PlayCommand.ExecuteAsync(null);
        await riser.PlayCommand.ExecuteAsync(null);   // the same button
        Assert.Equal(RiserState.Idle, riser.State);
        Assert.Equal(1, _library.Player.RiserStops);
        Assert.False(_library.Player.IsRiserActive);

        await riser.PlayCommand.ExecuteAsync(null);
        player.Stop();   // the stop button ends the whole thing
        await Task.Delay(400);
        Assert.Equal(RiserState.Idle, riser.State);

        await player.PlayPauseCommand.ExecuteAsync(null);
        await riser.PlayCommand.ExecuteAsync(null);
        player.PositionSeconds = 30;   // moving the playhead also cancels it, in the player
        Assert.Equal(TimeSpan.FromSeconds(30), _library.Player.Position);
    }

    [AvaloniaFact]
    public async Task Riser_without_a_known_tempo_does_not_wait_for_a_bar()
    {
        _library.AddPak(SongLocation.Enabled, "nobpm_P", Meta("No Tempo", "C") with { Bpm = null });
        var (vm, detail) = await SelectAsync("nobpm_P");
        using var _ = vm;
        await detail.Player!.PlayPauseCommand.ExecuteAsync(null);

        await detail.Player.Riser!.PlayCommand.ExecuteAsync(null);

        Assert.Null(_library.Player.RiserBar);   // the player starts it at once
    }

    [AvaloniaFact]
    public async Task A_song_without_a_riser_says_so_and_leaves_the_preview_alone()
    {
        _library.Media.RiserError = new InvalidDataException("This song has no riser.");
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        await player.PlayPauseCommand.ExecuteAsync(null);

        await player.Riser!.PlayCommand.ExecuteAsync(null);

        Assert.Equal(RiserState.Unavailable, player.Riser.State);
        Assert.Equal("This song has no riser.", player.Riser.Message);
        Assert.Equal("Riser", player.Riser.ToolTip);   // the hover text stays short; the reason is in Message
        Assert.False(player.Riser.PlayCommand.CanExecute(null));
        Assert.Equal(PlayerState.Playing, player.State);
        Assert.Null(_library.Player.Riser);   // nothing was asked of the player
    }

    [AvaloniaFact]
    public async Task A_riser_the_player_refuses_is_explained_and_the_song_goes_on()
    {
        var (vm, detail) = await SelectAsync("art_P");
        using var _ = vm;
        var player = detail.Player!;
        await player.PlayPauseCommand.ExecuteAsync(null);
        _library.Player.RiserFailure = new NotSupportedException("The riser could not be mixed with the song.");

        await player.Riser!.PlayCommand.ExecuteAsync(null);

        Assert.Equal(RiserState.Unavailable, player.Riser.State);
        Assert.Equal("The riser could not be mixed with the song.", player.Riser.Message);
        Assert.Equal(PlayerState.Playing, player.State);
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
