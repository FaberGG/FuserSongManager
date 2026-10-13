using System.Reflection;
using FuserSongManager.Infrastructure.Settings;
using Serilog;
using Serilog.Events;

namespace FuserSongManager.UI.Services;

/// <summary>Where the app keeps its data and how it logs (RF-SIS-01).</summary>
public static class AppEnvironment
{
    /// <summary><c>FSM_DATA_DIR</c> keeps settings, database and logs elsewhere, for trying the app without touching the real library.</summary>
    public static AppDataPaths DataPaths { get; } =
        Environment.GetEnvironmentVariable("FSM_DATA_DIR") is { Length: > 0 } dataDir ? new AppDataPaths(dataDir) : AppDataPaths.Default;

    public static string Version { get; } =
        typeof(AppEnvironment).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppEnvironment).Assembly.GetName().Version?.ToString()
        ?? "?";

    /// <summary>The app's own log. Silent until <see cref="StartLogging"/>.</summary>
    public static ILogger Log { get; private set; } = Serilog.Core.Logger.None;

    /// <summary>
    /// Opens the log file. Libraries that write to Serilog's static logger (CUE4Parse logs every pak it
    /// mounts) only reach the file with warnings and errors.
    /// </summary>
    public static void StartLogging()
    {
        var log = CreateLogger(DataPaths.LogsDirectory);
        Log = log;
        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .Filter.ByExcluding(IsKnownCustomPakNoise)
            .WriteTo.Logger(log)
            .CreateLogger();
    }

    /// <summary>
    /// Album art textures made by FuserCustomSongCreator keep the template's serial size, so CUE4Parse reports
    /// every one as "read incorrectly" although the image data is complete and correct (checked on 224 real paks).
    /// One warning and one error per selected song would bury real problems in the log.
    /// </summary>
    public static bool IsKnownCustomPakNoise(LogEvent e)
    {
        // Rendered with or without quotes depending on whether CUE4Parse passes the type as a template argument.
        var message = e.RenderMessage().Replace("\"", "");
        return message.StartsWith("Texture2D read incorrectly", StringComparison.Ordinal)
            || message.StartsWith("Could not read Texture2D named", StringComparison.Ordinal);
    }

    public static void StopLogging()
    {
        Serilog.Log.CloseAndFlush();
        (Log as IDisposable)?.Dispose();
    }

    /// <summary>One file per day, kept for two weeks and capped in size, so logs never fill the disk.</summary>
    public static ILogger CreateLogger(string logsDirectory) => new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.File(
            Path.Combine(logsDirectory, "fsm-.log"),
            restrictedToMinimumLevel: LogEventLevel.Information,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
}
