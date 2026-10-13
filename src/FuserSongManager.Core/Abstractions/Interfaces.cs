using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Abstractions;

/// <summary>Reads the game metadata of one pak. Must be thread-safe: the sync service calls it in parallel.</summary>
public interface ISongMetadataReader
{
    /// <summary>Never throws for a bad pak; returns a <see cref="ReadStatus.Failed"/> result instead.</summary>
    SongReadResult Read(string pakPath);
}

public interface ILibraryScanner
{
    LibraryScan Scan(LibraryPaths paths);
}

public interface ISongRepository
{
    Task<IReadOnlyList<Song>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates the game metadata and file state of each song. On existing rows the user's
    /// data (rating, notes, author) is left untouched, so a scan never overwrites an edit made meanwhile.
    /// </summary>
    Task SaveScanResultsAsync(IReadOnlyCollection<Song> songs, CancellationToken ct = default);

    Task DeleteAsync(IReadOnlyCollection<string> fileNames, CancellationToken ct = default);

    Task UpdateUserDataAsync(string fileName, int rating, string notes, string author, CancellationToken ct = default);
}

public interface ISongFileService
{
    /// <summary>
    /// Moves the song's .pak and .sig together to the folder of <paramref name="target"/>, keeping its subfolder.
    /// Never leaves a .pak without its .sig: if the second move fails the first one is undone (RF-EST-05).
    /// </summary>
    StateChangeResult Move(Song song, SongLocation target, LibraryPaths paths);

    /// <summary>Deletes <c>customSongsUnlocked_P.pak/.sig</c> so the game rebuilds its custom song cache (RF-EST-07).</summary>
    void ClearUnlockedSongsCache(LibraryPaths paths);
}

public interface IGameProcessService
{
    bool IsGameRunning();

    LaunchResult Launch(AppSettings settings);
}

public interface IArchiveImporter
{
    /// <param name="sources">Archives (.zip, .rar, .7z) or loose .pak files with their .sig alongside.</param>
    Task<ImportResult> ImportAsync(
        IReadOnlyList<string> sources,
        LibraryPaths paths,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>Reads the album art and the audio stored in a song pak. Thread-safe.</summary>
public interface ISongMediaReader
{
    /// <summary>The album art as PNG, or null if the pak has none (RF-PEN-14).</summary>
    byte[]? ReadAlbumArt(string pakPath);

    /// <summary>
    /// The song's parts in the key of <paramref name="mode"/> (its own key when null).
    /// Throws <see cref="NotSupportedException"/> or <see cref="InvalidDataException"/> with a clear message when the audio cannot be read.
    /// </summary>
    SongAudio ReadAudio(string pakPath, SongMode? mode);
}

/// <summary>Plays the parts of one song together, each of which can be muted (RF-PEN-13).</summary>
public interface IStemPlayer : IDisposable
{
    /// <summary>Replaces whatever was loaded. Starts paused at the beginning.</summary>
    void Load(SongAudio audio);

    void Play();
    void Pause();

    /// <summary>Pauses and goes back to the beginning.</summary>
    void Stop();

    bool IsPlaying { get; }
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }

    void SetMuted(StemKind stem, bool muted);

    /// <summary>Raised (on any thread) when playback reaches the end or the audio device stops it.</summary>
    event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;
}

/// <param name="Error">Set when the audio device failed instead of the song ending.</param>
public sealed class PlaybackEndedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
