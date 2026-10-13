using Avalonia;
using FuserSongManager.UI.Services;

namespace FuserSongManager.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        AppEnvironment.StartLogging();
        var log = AppEnvironment.Log;
        log.Information("Fuser Song Manager {Version} starting on {OS}, data folder {DataFolder}",
            AppEnvironment.Version, Environment.OSVersion, AppEnvironment.DataPaths.Root);

        // Last-resort handlers: errors on the UI thread are handled in App (the app keeps running);
        // these catch the rest so the cause is always in the log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Fatal(e.ExceptionObject as Exception, "Unhandled exception, the app is closing");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error(e.Exception, "Unobserved exception in a background task");
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            log.Information("Closed normally");
            return 0;
        }
        catch (Exception ex)
        {
            log.Fatal(ex, "The app crashed");
            return 1;
        }
        finally
        {
            AppEnvironment.StopLogging();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
