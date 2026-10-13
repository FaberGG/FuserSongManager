using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FuserSongManager.Infrastructure.Data;
using FuserSongManager.UI.Services;
using FuserSongManager.UI.ViewModels;
using FuserSongManager.UI.Views;

namespace FuserSongManager.UI;

public partial class App : Application
{
    private static Serilog.ILogger Log => AppEnvironment.Log;

    private IDialogService? _dialogs;
    private bool _showingError;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            try
            {
                var services = AppServices.CreateDefault(AppEnvironment.DataPaths, Log);
                desktop.MainWindow = string.IsNullOrWhiteSpace(services.Settings.FuserDirectory)
                    ? CreateFirstRunWindow(services, desktop)
                    : CreateMainWindow(services, desktop);
            }
            catch (Exception ex)
            {
                // E.g. the database is locked by another copy of the app or damaged: say so instead of vanishing.
                Log.Fatal(ex, "Could not start");
                var window = MessageWindow.Create("Fuser Song Manager could not start",
                    $"{ex.Message}\n\nIf another copy of Fuser Song Manager is open, close it and try again.\n\nDetails were saved in the log folder:\n{AppEnvironment.DataPaths.LogsDirectory}",
                    "Close", cancelText: null);
                window.ShowInTaskbar = true;
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                desktop.MainWindow = window;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>An unexpected error on the UI thread: log it, tell the user, and keep the app running (RF-UI-06).</summary>
    private async void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
        e.Handled = true;
        if (_dialogs is null || _showingError)
            return;

        _showingError = true;
        try
        {
            await _dialogs.ShowMessageAsync("Something went wrong",
                $"{e.Exception.Message}\n\nThe app can keep running. Details were saved in the log folder:\n{AppEnvironment.DataPaths.LogsDirectory}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not show the error message");
        }
        finally
        {
            _showingError = false;
        }
    }

    private Window CreateFirstRunWindow(AppServices services, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new FirstRunWindow();
        var dialogs = _dialogs = new AvaloniaDialogService(() => TopWindow(desktop, window));
        window.DataContext = new FirstRunViewModel(DetectV1(), dialogs);
        window.Completed += async (_, viewModel) =>
        {
            try
            {
                var settings = viewModel.ToSettings(services.Settings);
                services.SaveSettings(settings);
                Log.Information("First run: Fuser folder {FuserFolder}, launcher {Launcher}", settings.FuserDirectory, settings.LauncherType);
                if (viewModel.ImportV1 && viewModel.V1 is { } v1 && services.InsertV1Songs is { } insert)
                {
                    await insert(v1.Songs);
                    Log.Information("Imported {Count} songs from the v1 database {Path}", v1.Songs.Count, v1.DatabasePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "First run could not be completed");
                await dialogs.ShowMessageAsync("Could not finish the setup", $"{ex.Message}\n\nCheck the folder and try again.");
                window.AllowContinue();
                return;
            }

            var main = CreateMainWindow(services, desktop);
            desktop.MainWindow = main;
            main.Show();
            window.Close();
        };
        return window;
    }

    private MainWindow CreateMainWindow(AppServices services, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new MainWindow();
        var dialogs = _dialogs = new AvaloniaDialogService(() => TopWindow(desktop, window));
        window.DataContext = new MainViewModel(services, dialogs, new ShellService());
        return window;
    }

    /// <summary>Dialogs open over the newest visible window, so a picker opened from Settings sits on top of Settings.</summary>
    private static Window TopWindow(IClassicDesktopStyleApplicationLifetime desktop, Window fallback) =>
        desktop.Windows.LastOrDefault(w => w.IsVisible) ?? fallback;

    /// <summary>The v1 library, if v1 was installed. A damaged v1 database must not stop the first run.</summary>
    private static V1Library? DetectV1()
    {
        try
        {
            var install = V1Import.Detect();
            if (install is null)
                return null;
            var library = new V1Library(
                install.DatabasePath,
                install.Config is { } config ? V1Import.ToSettings(config) : null,
                V1Import.ReadSongs(install.DatabasePath));
            Log.Information("Found v1 library {Path} with {Count} songs", library.DatabasePath, library.Songs.Count);
            return library;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The v1 library could not be read; continuing without it");
            return null;
        }
    }
}
