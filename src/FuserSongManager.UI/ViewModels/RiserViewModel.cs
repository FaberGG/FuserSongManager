using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.UI.ViewModels;

public enum RiserState
{
    /// <summary>Ready; the pak is only read the first time the button is pressed.</summary>
    Idle,
    Loading,
    /// <summary>Asked for, waiting for the end of the bar, or sounding: until the song takes over.</summary>
    Playing,
    /// <summary>The song has no riser (or it could not be played): see <see cref="RiserViewModel.Message"/>.</summary>
    Unavailable,
}

/// <summary>
/// The riser of a song: the transition effect the game plays to go from this song to the next, which is different
/// for every custom song. Here it goes from the song to itself: the riser comes in on the next bar, the song fades
/// out under it over two bars, and the song starts again from its beginning the moment the riser ends. The
/// preview's own player does all of that in one stream (see <see cref="IStemPlayer.PlayRiser"/>), so the
/// hand-over has no gap. The riser's audio is read on the first press and kept.
/// </summary>
public sealed partial class RiserViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly Func<SongAudio> _loadAudio;
    private readonly Serilog.ILogger _log;
    private readonly string _fileName;
    private readonly Func<Task<IStemPlayer?>> _preparePlayer;
    private readonly Func<TimeSpan?> _barLength;
    private readonly Action _started;
    private readonly CancellationTokenSource _lifetime = new();
    private SongAudio? _audio;
    private IStemPlayer? _player;

    /// <param name="preparePlayer">The preview's player with the song loaded (loading it if needed); null when the song cannot be played.</param>
    /// <param name="barLength">One bar of the song; null when its tempo is not known.</param>
    /// <param name="started">Called once the riser is under way: the preview is now playing.</param>
    public RiserViewModel(string fileName, Func<SongAudio> loadAudio, Serilog.ILogger log,
        Func<Task<IStemPlayer?>> preparePlayer, Func<TimeSpan?> barLength, Action started)
    {
        _fileName = fileName;
        _loadAudio = loadAudio;
        _log = log;
        _preparePlayer = preparePlayer;
        _barLength = barLength;
        _started = started;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(IsLoading), nameof(ToolTip))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    public partial RiserState State { get; private set; } = RiserState.Idle;

    /// <summary>Why the riser cannot be heard, or what went wrong.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    public bool IsPlaying => State == RiserState.Playing;
    public bool IsLoading => State == RiserState.Loading;

    /// <summary>The button's hover text and accessible name: what pressing it does now. Why a riser is unavailable is shown in <see cref="Message"/>.</summary>
    public string ToolTip => State == RiserState.Playing ? "Cancel" : "Riser";

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task Play()
    {
        if (State == RiserState.Playing)
        {
            _player?.StopRiser();
            State = RiserState.Idle;
            return;
        }

        Message = null;
        State = RiserState.Loading;
        try
        {
            // Opening the pak and decrypting takes a moment; keep the window responsive.
            _audio ??= await Task.Run(_loadAudio);
            if (_lifetime.IsCancellationRequested)
                return;

            // The song is loaded here when it was not playing yet: the riser leads into it.
            var player = await _preparePlayer();
            if (player is null || _lifetime.IsCancellationRequested)
            {
                State = RiserState.Idle;   // the preview already says why the song cannot be played
                return;
            }

            player.PlayRiser(_audio, _barLength());
            _player = player;
            _log.Information("Riser of {FileName}: {Stems}", _fileName, string.Join(", ", _audio.Stems.Select(s => s.Kind)));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Riser of {FileName} is not available", _fileName);
            Message = ex is NotSupportedException or InvalidDataException
                ? ex.Message
                : $"The riser could not be played: {ex.Message}";
            State = RiserState.Unavailable;
            return;
        }

        State = RiserState.Playing;
        _started();
        _ = FollowPlaybackAsync(_lifetime.Token);
    }

    private bool CanPlay() => State is RiserState.Idle or RiserState.Playing;

    /// <summary>Notices when the song has taken over, or the riser was cancelled by stopping or moving the playhead.</summary>
    private async Task FollowPlaybackAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (State == RiserState.Playing && await timer.WaitForNextTickAsync(ct))
            {
                if (_player is { IsRiserActive: true })
                    continue;
                State = RiserState.Idle;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _player = null;   // the preview owns it
    }
}
