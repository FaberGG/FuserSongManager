using Avalonia.Headless.XUnit;
using FuserSongManager.Core.Models;
using FuserSongManager.UI.Services;
using FuserSongManager.UI.ViewModels;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using static FuserSongManager.UI.Tests.TestLibrary;

namespace FuserSongManager.UI.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly TestLibrary _library = new();
    private readonly CollectingSink _sink = new();

    public void Dispose() => _library.Dispose();

    private MainViewModel CreateViewModel()
    {
        var logged = new AppServices(_library.Settings, _library.Repository, new Core.Services.LibraryScanner(), _library.Reader,
            new Infrastructure.Files.SongFileService(), _library.Game, new Infrastructure.Files.SharpCompressImporter(new Core.Services.LibraryScanner()))
        {
            Log = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger(),
            LogsDirectory = Path.Combine(_library.Root, "logs"),
        };
        return new MainViewModel(logged, _library.Dialogs, _library.Shell);
    }

    [AvaloniaFact]
    public async Task Every_unreadable_pak_is_logged_with_its_name_and_path()
    {
        _library.AddPak(SongLocation.Enabled, "good_P", Meta("Good", "Band"));
        var broken = _library.AddPak(SongLocation.Enabled, "broken_P", subfolder: "Pack");

        using var vm = CreateViewModel();
        await vm.InitializeAsync();

        Assert.Contains(_sink.Messages, m => m.StartsWith("Library shown: 0 songs"));
        Assert.Contains(_sink.Messages, m => m.StartsWith("Sync in") && m.Contains("2 new") && m.Contains("1 with problems"));
        var warning = Assert.Single(_sink.Events, e => e.Level == LogEventLevel.Warning);
        var text = warning.RenderMessage();
        Assert.Contains("broken_P", text);
        Assert.Contains(broken, text);
        Assert.Contains("not a valid Fuser song", text);
    }

    [AvaloniaFact]
    public async Task Failed_moves_and_unexpected_errors_are_logged()
    {
        _library.AddPak(SongLocation.Disabled, "nosig_P", Meta("No Sig", "X"), withSig: false);
        using var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.SetSelection([vm.Rows.Single()]);

        await vm.EnableSelectedCommand.ExecuteAsync(null);

        Assert.Contains(_sink.Messages, m => m.Contains("Could not enable \"nosig_P\"") && m.Contains("MissingSignature"));
    }

    [Fact]
    public void Settings_offer_the_log_folder()
    {
        var settings = new SettingsViewModel(new AppSettings(), _library.Dialogs)
        {
            LogsDirectory = Path.Combine(_library.Root, "logs"),
            Shell = _library.Shell,
        };

        settings.OpenLogsCommand.Execute(null);

        Assert.True(settings.CanOpenLogs);
        Assert.Equal(settings.LogsDirectory, Assert.Single(_library.Shell.Shown));
        Assert.True(Directory.Exists(settings.LogsDirectory));
    }

    [Fact]
    public void Log_files_are_written_to_the_logs_folder()
    {
        var folder = Path.Combine(_library.Root, "logs");
        using (var logger = (Logger)AppEnvironment.CreateLogger(folder))
            logger.Warning("Could not read {FileName}", "x_P");

        var file = Assert.Single(Directory.GetFiles(folder, "fsm-*.log"));
        Assert.Contains("[WRN] Could not read x_P", File.ReadAllText(file));   // :lj renders strings without quotes
    }

    [Theory]
    [InlineData("Texture2D read incorrectly. Offset 176401, Skip Offset 45251, Bytes remaining -131150", true)]
    [InlineData("Could not read \"Texture2D\" named \"T_Song_small\" correctly", true)]
    [InlineData("Could not read \"SoundWave\" named \"x\" correctly", false)]
    [InlineData("Failed to mount pak", false)]
    public void Only_the_known_album_art_noise_from_CUE4Parse_is_dropped(string message, bool dropped)
    {
        var e = new LogEvent(DateTimeOffset.Now, LogEventLevel.Warning, null, new Serilog.Parsing.MessageTemplateParser().Parse(message), []);
        Assert.Equal(dropped, AppEnvironment.IsKnownCustomPakNoise(e));
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public IEnumerable<string> Messages => Events.Select(e => e.RenderMessage());
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
