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

/// <summary>One part of the song with its on/off switch, shown as a disc like in the game.</summary>
public sealed partial class StemToggleViewModel(StemKind kind, string? instrument, Action<StemKind, bool> onChanged) : ViewModelBase
{
    public StemKind Kind { get; } = kind;
    public string Name { get; } = kind.ToString();

    /// <summary>The game's instrument name for this part ("Drums", "Synth"…), or null if unknown.</summary>
    public string? Instrument { get; } = instrument;
    public string InstrumentName => Display.Instrument(Kind, Instrument);
    public string? IconKey => Display.InstrumentIconKey(Instrument);

    /// <summary>False when the song has no audio for this part (e.g. the author left the bass empty).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip), nameof(IsSpinning))]
    public partial bool IsAvailable { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpinning), nameof(ToolTip))]
    public partial bool IsOn { get; set; } = true;

    /// <summary>Set by the player: the disc turns while its part is heard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpinning))]
    public partial bool IsPlaying { get; set; }

    public bool IsSpinning => IsPlaying && IsOn && IsAvailable;

    public string ToolTip =>
        !IsAvailable ? $"This song has no {Name.ToLowerInvariant()} part"
        : IsOn ? $"Mute the {Name.ToLowerInvariant()}"
        : $"Turn the {Name.ToLowerInvariant()} back on";

    partial void OnIsOnChanged(bool value) => onChanged(Kind, value);
}

/// <summary>
/// Preview of a song (RF-PEN-13): its four parts played together and looped, each of which can be turned
/// off, like mixing in the game. The audio is read from the pak the first time play is pressed.
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

    public PlayerViewModel(string fileName, Func<SongAudio> loadAudio, Func<IStemPlayer> createPlayer, Serilog.ILogger log,
        SongInstruments? instruments = null, int? bpm = null)
    {
        _fileName = fileName;
        _loadAudio = loadAudio;
        _createPlayer = createPlayer;
        _log = log;
        Bpm = bpm is > 0 ? bpm : null;
        Stems = Enum.GetValues<StemKind>().Select(kind => new StemToggleViewModel(kind, instruments?.For(kind), OnStemToggled)).ToList();
    }

    /// <summary>Beats per minute, to count bars; null when the pak does not say.</summary>
    public int? Bpm { get; }

    public IReadOnlyList<StemToggleViewModel> Stems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseText), nameof(IsPlaying), nameof(IsLoading), nameof(CanSeek), nameof(BarText))]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand), nameof(StopCommand))]
    public partial PlayerState State { get; private set; } = PlayerState.Idle;

    partial void OnStateChanged(PlayerState value)
    {
        foreach (var stem in Stems)
            stem.IsPlaying = value == PlayerState.Playing;
    }

    /// <summary>Why the preview cannot play, or what went wrong.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText), nameof(CurrentBar), nameof(CurrentPhrase), nameof(BarText))]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText), nameof(BarCount), nameof(PhraseCount), nameof(CurrentBar), nameof(BarText))]
    public partial double DurationSeconds { get; private set; }

    public bool IsPlaying => State == PlayerState.Playing;
    public bool IsLoading => State == PlayerState.Loading;
    public bool CanSeek => State is PlayerState.Playing or PlayerState.Paused;
    public string PlayPauseText => State == PlayerState.Playing ? "Pause" : State == PlayerState.Loading ? "Loading…" : "Play";
    public string TimeText => $"{Format(PositionSeconds)} / {Format(DurationSeconds)}";

    /// <summary>A bar is four beats. Fuser songs are 32 bars: four phrases of eight.</summary>
    public const int BarsPerPhrase = 8;

    private double? BarSeconds => Bpm is { } bpm ? 4 * 60.0 / bpm : null;

    /// <summary>Bars in the song, from its length and tempo; 0 until the audio is loaded or without a tempo.</summary>
    public int BarCount => BarSeconds is { } bar && DurationSeconds > 0 ? Math.Max(1, (int)Math.Round(DurationSeconds / bar)) : 0;

    /// <summary>1-based bar being played.</summary>
    public int CurrentBar => BarSeconds is { } bar && BarCount > 0 ? Math.Clamp((int)(PositionSeconds / bar) + 1, 1, BarCount) : 0;

    public int PhraseCount => (BarCount + BarsPerPhrase - 1) / BarsPerPhrase;
    public int CurrentPhrase => (CurrentBar - 1) / BarsPerPhrase + 1;

    /// <summary>"Bar 12 of 32 · Phrase 2 of 4", or a hint before the song is loaded.</summary>
    public string BarText =>
        State == PlayerState.Loading ? "Reading the song…"
        : BarCount == 0 ? (State == PlayerState.Idle ? "Plays in a loop, like in the game" : "")
        : $"Bar {CurrentBar} of {BarCount} · Phrase {CurrentPhrase} of {PhraseCount}";

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
            player.Loop = true;
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
