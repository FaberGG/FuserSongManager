using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;
using FuserSongManager.Infrastructure.Audio;
using FuserSongManager.Infrastructure.Data;
using FuserSongManager.Infrastructure.Files;
using FuserSongManager.Infrastructure.Pak;
using FuserSongManager.Infrastructure.Settings;

namespace FuserSongManager.UI.Services;

/// <summary>
/// Composition root: builds every service once and holds the current settings.
/// Infrastructure pieces come in through interfaces so tests can swap them for fakes.
/// </summary>
public sealed class AppServices
{
    private readonly ISettingsStore _settingsStore;

    public AppServices(
        ISettingsStore settingsStore,
        ISongRepository repository,
        ILibraryScanner scanner,
        ISongMetadataReader reader,
        ISongFileService files,
        IGameProcessService game,
        IArchiveImporter importer)
    {
        _settingsStore = settingsStore;
        Settings = settingsStore.Load();
        Repository = repository;
        Game = game;
        Sync = new LibrarySyncService(scanner, reader, repository);
        State = new SongStateService(files, repository, game);
        Import = new SongImportService(importer, game, files, Sync);
        Launcher = new GameLaunchService(game, files);
    }

    public AppSettings Settings { get; private set; }
    public ISongRepository Repository { get; }
    public IGameProcessService Game { get; }
    public LibrarySyncService Sync { get; }
    public SongStateService State { get; }
    public SongImportService Import { get; }
    public GameLaunchService Launcher { get; }

    /// <summary>Events and errors for the log file (RF-SIS-01). Silent unless the app sets one.</summary>
    public Serilog.ILogger Log { get; init; } = Serilog.Core.Logger.None;

    /// <summary>Folder of the log files, offered in Settings. Null when not logging to files.</summary>
    public string? LogsDirectory { get; init; }

    /// <summary>Album art and audio of the paks. Null disables both (the detail panel then shows neither).</summary>
    public ISongMediaReader? Media { get; init; }

    /// <summary>Where extracted album art is cached. Null keeps nothing on disk.</summary>
    public string? ArtCacheDirectory { get; init; }

    /// <summary>Creates the audio player for the preview. Null hides the player.</summary>
    public Func<IStemPlayer>? CreatePlayer { get; init; }

    private AlbumArtService? _albumArt;

    public AlbumArtService? AlbumArt => Media is null ? null : _albumArt ??= new AlbumArtService(Media, ArtCacheDirectory);

    /// <summary>Songs read by v1, to be added on first run. Null outside the first-run flow.</summary>
    public Func<IReadOnlyCollection<Song>, Task>? InsertV1Songs { get; init; }

    public bool IsConfigured => FuserInstall.IsInstallFolder(Settings.FuserDirectory);

    /// <summary>The library folders, or null while the Fuser folder is not set or not found (e.g. a disconnected drive).</summary>
    public LibraryPaths? Paths => IsConfigured ? LibraryPaths.FromSettings(Settings) : null;

    public void SaveSettings(AppSettings settings)
    {
        _settingsStore.Save(settings);
        Settings = settings;
        if (Paths is { } paths)
        {
            Directory.CreateDirectory(paths.EnabledDirectory);
            Directory.CreateDirectory(paths.DisabledDirectory);
        }
    }

    public static AppServices CreateDefault(AppDataPaths dataPaths, Serilog.ILogger log)
    {
        var database = new SqliteDatabase(dataPaths.DatabaseFile);
        database.Migrate();
        var repository = new SqliteSongRepository(database);
        var scanner = new LibraryScanner();

        return new AppServices(
            new JsonSettingsStore(dataPaths),
            repository,
            scanner,
            new CueSongMetadataReader(PatchedDlcCatalog.LoadEmbedded()),
            new SongFileService(),
            new GameProcessService(),
            new SharpCompressImporter(scanner))
        {
            InsertV1Songs = songs => repository.InsertIfMissingAsync(songs),
            Log = log,
            LogsDirectory = dataPaths.LogsDirectory,
            Media = new PakMediaReader(),
            ArtCacheDirectory = dataPaths.ArtCacheDirectory,
            CreatePlayer = () => new NAudioStemPlayer(),
        };
    }
}
