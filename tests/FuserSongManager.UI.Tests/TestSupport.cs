using Avalonia;
using Avalonia.Headless;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;
using FuserSongManager.Infrastructure.Data;
using FuserSongManager.Infrastructure.Files;
using FuserSongManager.UI.Services;
using FuserSongManager.UI.ViewModels;
using Microsoft.Data.Sqlite;

[assembly: AvaloniaTestApplication(typeof(FuserSongManager.UI.Tests.TestAppBuilder))]

namespace FuserSongManager.UI.Tests;

public static class TestAppBuilder
{
    // Skia rendering (not headless drawing) so tests can capture real screenshots of the windows.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithAppFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// A throwaway Fuser install with real SQLite, scanner, file moves and importer; only the pak reader,
/// the game process and the dialogs are fake.
/// </summary>
public sealed class TestLibrary : IDisposable
{
    public TestLibrary()
    {
        Directory.CreateDirectory(Root);
        FuserDirectory = Path.Combine(Root, "Fuser Game");
        Paths = LibraryPaths.FromFuserDirectory(FuserDirectory);
        Directory.CreateDirectory(Paths.EnabledDirectory);
        Directory.CreateDirectory(Paths.DisabledDirectory);
        Database = new SqliteDatabase(Path.Combine(Root, "songs.sqlite"));
        Database.Migrate();
        Repository = new SqliteSongRepository(Database);
        Settings = new InMemorySettingsStore(new AppSettings { FuserDirectory = FuserDirectory });
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fsm-ui-tests-" + Guid.NewGuid().ToString("N"));
    public string FuserDirectory { get; }
    public LibraryPaths Paths { get; }
    public SqliteDatabase Database { get; }
    public SqliteSongRepository Repository { get; }
    public InMemorySettingsStore Settings { get; }
    public FakeReader Reader { get; } = new();
    public FakeGame Game { get; } = new();
    public FakeDialogs Dialogs { get; } = new();
    public FakeShell Shell { get; } = new();
    public FakeMedia Media { get; } = new();
    public FakeStemPlayer Player { get; } = new();

    public string AddPak(SongLocation location, string fileName, SongMetadata? metadata = null, bool withSig = true, string? subfolder = null)
    {
        var folder = Path.Combine(Paths.RootOf(location), subfolder ?? "");
        Directory.CreateDirectory(folder);
        var pak = Path.Combine(folder, fileName + ".pak");
        File.WriteAllText(pak, "pak " + fileName);
        if (withSig)
            File.WriteAllText(Path.ChangeExtension(pak, ".sig"), "sig");
        if (metadata is not null)
            Reader.Metadata[fileName] = metadata;
        return pak;
    }

    public AppServices CreateServices()
    {
        var scanner = new LibraryScanner();
        return new AppServices(Settings, Repository, scanner, Reader, new SongFileService(), Game,
            new SharpCompressImporter(scanner, Path.Combine(Root, "import-work")))
        {
            Media = Media,
            ArtCacheDirectory = Path.Combine(Root, "art"),
            CreatePlayer = () => Player,
        };
    }

    public MainViewModel CreateMainViewModel() => new(CreateServices(), Dialogs, Shell);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the test host; the OS temp cleanup will take it.
        }
    }

    public static SongMetadata Meta(string title, string artist, Genre genre = Genre.Rock, int year = 2000, int bpm = 120) =>
        new(title, artist, year, genre, MusicalKey.A, SongMode.Minor, bpm);
}

public sealed class InMemorySettingsStore(AppSettings settings) : ISettingsStore
{
    public AppSettings Current { get; private set; } = settings;
    public AppSettings Load() => Current;
    public void Save(AppSettings settings) => Current = settings;
}

public sealed class FakeReader : ISongMetadataReader
{
    /// <summary>What every readable fake pak says its parts are played with.</summary>
    public static readonly SongInstruments Instruments = new("Drums", "Guitar", "Synth", "Vocal");

    public Dictionary<string, SongMetadata> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SongReadResult Read(string pakPath)
    {
        var name = Path.GetFileNameWithoutExtension(pakPath);
        return Metadata.TryGetValue(name, out var metadata)
            ? new SongReadResult(metadata, ReadStatus.Ok, null, Instruments: Instruments)
            : SongReadResult.Failed("The pak is not a valid Fuser song.");
    }
}

public sealed class FakeGame : IGameProcessService
{
    public bool Running { get; set; }
    public int Launches { get; private set; }
    public bool IsGameRunning() => Running;

    public LaunchResult Launch(AppSettings settings)
    {
        Launches++;
        return new LaunchResult(LaunchOutcome.Started);
    }
}

public sealed class FakeDialogs : IDialogService
{
    public List<(string Title, string Message)> Messages { get; } = [];
    public List<string> Confirmations { get; } = [];
    public bool ConfirmAnswer { get; set; } = true;
    public IReadOnlyList<string> ImportSources { get; set; } = [];
    public Func<SettingsViewModel, bool>? OnSettings { get; set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        Confirmations.Add(message);
        return Task.FromResult(ConfirmAnswer);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> PickImportSourcesAsync() => Task.FromResult(ImportSources);
    public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult<string?>(null);
    public Task<string?> PickExecutableAsync(string? startFolder) => Task.FromResult<string?>(null);
    public Task<bool> ShowSettingsAsync(SettingsViewModel settings) => Task.FromResult(OnSettings?.Invoke(settings) ?? false);
}

public sealed class FakeShell : IShellService
{
    public List<string> Shown { get; } = [];
    public void ShowInFileManager(string path) => Shown.Add(path);
    public void OpenFolder(string folder) => Shown.Add(folder);
}

public sealed class FakeMedia : ISongMediaReader
{
    /// <summary>Album art by pak file name (without extension).</summary>
    public Dictionary<string, byte[]> Art { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<StemKind> Stems { get; } = [.. Enum.GetValues<StemKind>()];
    public Exception? AudioError { get; set; }
    public List<(string Pak, SongMode? Mode)> AudioReads { get; } = [];

    public byte[]? ReadAlbumArt(string pakPath) => Art.GetValueOrDefault(Path.GetFileNameWithoutExtension(pakPath));

    public SongAudio ReadAudio(string pakPath, SongMode? mode)
    {
        AudioReads.Add((pakPath, mode));
        if (AudioError is not null)
            throw AudioError;
        return new SongAudio(Path.GetFileNameWithoutExtension(pakPath), Stems.Order().Select(k => new SongStem(k, mode?.ToString() ?? "", [1])).ToList());
    }

    /// <summary>A solid-colour PNG, as the real reader returns.</summary>
    public static byte[] Png(SkiaSharp.SKColor colour, int size = 64)
    {
        using var bitmap = new SkiaSharp.SKBitmap(size, size);
        bitmap.Erase(colour);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        return image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray();
    }
}

public sealed class FakeStemPlayer : IStemPlayer
{
    public SongAudio? Loaded { get; private set; }
    public HashSet<StemKind> Muted { get; } = [];
    public bool IsPlaying { get; private set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration => TimeSpan.FromSeconds(61);
    public bool Disposed { get; private set; }
    public int Plays { get; private set; }
    public bool Loop { get; set; }

    public event EventHandler<PlaybackEndedEventArgs>? PlaybackEnded;

    public void Load(SongAudio audio) => Loaded = audio;
    public void Play() { IsPlaying = true; Plays++; }
    public void Pause() => IsPlaying = false;
    public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; }
    public void SetMuted(StemKind stem, bool muted) { if (muted) Muted.Add(stem); else Muted.Remove(stem); }
    public void Dispose() { Disposed = true; IsPlaying = false; }

    public void End(Exception? error = null) { IsPlaying = false; PlaybackEnded?.Invoke(this, new PlaybackEndedEventArgs(error)); }
}
