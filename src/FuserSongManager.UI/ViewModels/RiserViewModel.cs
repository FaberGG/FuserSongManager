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
    Playing,
    /// <summary>The song has no riser (or it could not be played): see <see cref="RiserViewModel.Message"/>.</summary>
    Unavailable,
}

/// <summary>
/// The riser of a song: the transition effect the game plays to go from this song to the next, which is different
/// for every custom song. It plays once, on its own, in the key of the song, through a player of its own, so the
/// preview's position and muted parts are left as they were. The audio is read on the first press and kept.
/// </summary>
public sealed partial class RiserViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly Func<SongAudio> _loadAudio;
    private readonly Func<IStemPlayer> _createPlayer;
    private readonly Serilog.ILogger _log;
    private readonly string _fileName;
    private readonly Action _beforePlay;
    private readonly CancellationTokenSource _lifetime = new();
    private IStemPlayer? _player;
    private volatile PlaybackEndedEventArgs? _ended;

    /// <param name="beforePlay">Called before the riser starts: the preview of the song stops so the riser is heard alone.</param>
    public RiserViewModel(string fileName, Func<SongAudio> loadAudio, Func<IStemPlayer> createPlayer, Serilog.ILogger log, Action beforePlay)
    {
        _fileName = fileName;
        _loadAudio = loadAudio;
        _createPlayer = createPlayer;
        _log = log;
        _beforePlay = beforePlay;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(IsLoading), nameof(ToolTip))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    public partial RiserState State { get; private set; } = RiserState.Idle;

    /// <summary>Why the riser cannot be heard, or what went wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial string? Message { get; private set; }

    public bool IsPlaying => State == RiserState.Playing;
    public bool IsLoading => State == RiserState.Loading;

    public string ToolTip =>
        State == RiserState.Unavailable ? Message ?? "This song has no riser"
        : State == RiserState.Playing ? "Stop the riser"
        : "Listen to the riser: the effect the game plays to go from this song to the next";

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task Play()
    {
        if (State == RiserState.Playing)
        {
            Stop();
            return;
        }

        Message = null;

        if (_player is null)
        {
            State = RiserState.Loading;
            try
            {
                // Opening the pak and decrypting takes a moment; keep the window responsive.
                var audio = await Task.Run(_loadAudio);
                if (_lifetime.IsCancellationRequested)
                    return;

                var player = _createPlayer();
                player.Loop = false;
                player.Load(audio);
                player.PlaybackEnded += (_, e) => _ended = e;
                _player = player;
                _log.Information("Riser of {FileName}: {Stems}, {Duration:0.0} s", _fileName,
                    string.Join(", ", audio.Stems.Select(s => s.Kind)), player.Duration.TotalSeconds);
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
        }
        else
        {
            _player.Stop();   // back to the start for another listen
        }

        _ended = null;
        try
        {
            // Only now: a song without a riser must not interrupt the preview.
            _beforePlay();
            _player.Play();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Riser of {FileName} could not be played", _fileName);
            Message = $"The riser could not be played: {ex.Message}";
            State = RiserState.Idle;
            return;
        }
        State = RiserState.Playing;
        _ = FollowPlaybackAsync(_lifetime.Token);
    }

    private bool CanPlay() => State is RiserState.Idle or RiserState.Playing;

    /// <summary>Stops the riser if it is playing (the song's preview starting, or another press of the button).</summary>
    public void Stop()
    {
        if (State != RiserState.Playing)
            return;
        _player?.Stop();
        State = RiserState.Idle;
    }

    /// <summary>Notices when the riser ends (the player reports it on its own thread).</summary>
    private async Task FollowPlaybackAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (State == RiserState.Playing && await timer.WaitForNextTickAsync(ct))
            {
                if (_ended is not { } ended)
                    continue;
                _ended = null;
                if (ended.Error is not null)
                {
                    _log.Warning(ended.Error, "Audio device stopped the riser of {FileName}", _fileName);
                    Message = $"The audio device stopped: {ended.Error.Message}";
                }
                _player?.Stop();
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
        _player?.Dispose();
        _player = null;
    }
}
