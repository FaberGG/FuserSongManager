using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.UI.ViewModels;

public enum PlayerState
{
    /// <summary>Nothing loaded yet: the pak is only read when the user presses play.</summary>
    Idle,
    Loading,
    Playing,
    Paused,
    Unavailable,
}

/// <summary>One part of the song with its on/off switch.</summary>
public sealed partial class StemToggleViewModel(StemKind kind, Action<StemKind, bool> onChanged) : ViewModelBase
{
    public StemKind Kind { get; } = kind;
    public string Name { get; } = kind.ToString();

    /// <summary>False when the song has no audio for this part (e.g. the author left the bass empty).</summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; set; } = true;

    [ObservableProperty]
    public partial bool IsOn { get; set; } = true;

    public string ToolTip => IsAvailable ? $"Turn the {Name.ToLowerInvariant()} on or off" : $"This song has no {Name.ToLowerInvariant()} part";

    partial void OnIsOnChanged(bool value) => onChanged(Kind, value);
    partial void OnIsAvailableChanged(bool value) => OnPropertyChanged(nameof(ToolTip));
}

/// <summary>
/// Preview of a song (RF-PEN-13): its four parts played together, each of which can be turned off,
/// like mixing in the game. The audio is read from the pak the first time play is pressed.
/// </summary>
public sealed partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly Func<SongAudio> _loadAudio;
    private readonly Func<IStemPlayer> _createPlayer;
    private readonly Serilog.ILogger _log;
    private readonly string _fileName;
    private readonly CancellationTokenSource _lifetime = new();
    private IStemPlayer? _player;
    private volatile PlaybackEndedEventArgs? _ended;
    private bool _syncingPosition;

    public PlayerViewModel(string fileName, Func<SongAudio> loadAudio, Func<IStemPlayer> createPlayer, Serilog.ILogger log)
    {
        _fileName = fileName;
        _loadAudio = loadAudio;
        _createPlayer = createPlayer;
        _log = log;
        Stems = Enum.GetValues<StemKind>().Select(kind => new StemToggleViewModel(kind, OnStemToggled)).ToList();
    }

    public IReadOnlyList<StemToggleViewModel> Stems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseText), nameof(IsPlaying), nameof(CanSeek))]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand), nameof(StopCommand))]
    public partial PlayerState State { get; private set; } = PlayerState.Idle;

    /// <summary>Why the preview cannot play, or what went wrong.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial double DurationSeconds { get; private set; }

    public bool IsPlaying => State == PlayerState.Playing;
    public bool CanSeek => State is PlayerState.Playing or PlayerState.Paused;
    public string PlayPauseText => State == PlayerState.Playing ? "❚❚  Pause" : State == PlayerState.Loading ? "Loading…" : "▶  Play";
    public string TimeText => $"{Format(PositionSeconds)} / {Format(DurationSeconds)}";

    private static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss");

    partial void OnPositionSecondsChanged(double value)
    {
        // Moved by the user (slider), not by the playback clock.
        if (!_syncingPosition && _player is not null && CanSeek)
            _player.Position = TimeSpan.FromSeconds(value);
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private async Task PlayPause()
    {
        switch (State)
        {
            case PlayerState.Playing:
                _player!.Pause();
                State = PlayerState.Paused;
                return;
            case PlayerState.Paused:
                StartPlaying();
                return;
        }

        State = PlayerState.Loading;
        Message = null;
        try
        {
            // Opening the pak and decrypting takes a moment; keep the window responsive.
            var audio = await Task.Run(_loadAudio);
            if (_lifetime.IsCancellationRequested)
                return;

            var player = _createPlayer();
            player.Load(audio);
            player.PlaybackEnded += (_, e) => _ended = e;
            _player = player;

            var present = audio.Stems.Select(s => s.Kind).ToHashSet();
            foreach (var stem in Stems)
            {
                stem.IsAvailable = present.Contains(stem.Kind);
                player.SetMuted(stem.Kind, !stem.IsOn);
            }
            DurationSeconds = player.Duration.TotalSeconds;
            _log.Information("Preview of {FileName}: {Stems}", _fileName, string.Join(", ", audio.Stems.Select(s => $"{s.Kind} ({s.ZoneLabel})")));
            StartPlaying();
            _ = FollowPlaybackAsync(_lifetime.Token);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Preview of {FileName} is not available", _fileName);
            Message = ex is NotSupportedException or InvalidDataException
                ? ex.Message
                : $"The audio could not be played: {ex.Message}";
            State = PlayerState.Unavailable;
            _player?.Dispose();
            _player = null;
        }
    }

    private bool CanPlayPause() => State is not PlayerState.Loading and not PlayerState.Unavailable;

    [RelayCommand(CanExecute = nameof(CanSeek))]
    public void Stop()
    {
        if (_player is null || !CanSeek)
            return;
        _player.Stop();
        State = PlayerState.Paused;
        SetPosition(0);
    }

    private void StartPlaying()
    {
        _ended = null;
        _player!.Play();
        State = PlayerState.Playing;
    }

    private void OnStemToggled(StemKind kind, bool on) => _player?.SetMuted(kind, !on);

    /// <summary>Moves the slider with the music and notices when the song ends (the player reports it on its own thread).</summary>
    private async Task FollowPlaybackAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_player is null)
                    return;
                if (_ended is { } ended)
                {
                    _ended = null;
                    if (ended.Error is not null)
                    {
                        _log.Warning(ended.Error, "Audio device stopped the preview of {FileName}", _fileName);
                        Message = $"The audio device stopped: {ended.Error.Message}";
                    }
                    _player.Stop();
                    State = PlayerState.Paused;
                    SetPosition(0);
                    continue;
                }
                if (State == PlayerState.Playing)
                    SetPosition(_player.Position.TotalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetPosition(double seconds)
    {
        _syncingPosition = true;
        PositionSeconds = seconds;
        _syncingPosition = false;
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _player?.Dispose();
        _player = null;
    }
}
