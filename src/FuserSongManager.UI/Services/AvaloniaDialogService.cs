using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FuserSongManager.UI.Controls;
using FuserSongManager.UI.ViewModels;
using FuserSongManager.UI.Views;

namespace FuserSongManager.UI.Services;

/// <summary>
/// Dialogs for the window returned by <paramref name="owner"/>. Messages and settings are shown inside the main window
/// (its <see cref="DialogHost"/>); only the file pickers, which belong to the operating system, and the messages of
/// windows without a host (the first-run wizard) open windows of their own.
/// </summary>
public sealed class AvaloniaDialogService(Func<Window> owner) : IDialogService
{
    private static readonly FilePickerFileType SongFiles = new("Songs (.zip, .rar, .7z, .pak)")
    {
        Patterns = ["*.zip", "*.rar", "*.7z", "*.pak"],
    };

    private static readonly FilePickerFileType Executables = new("Programs (.exe, .bat, .cmd)")
    {
        Patterns = ["*.exe", "*.bat", "*.cmd"],
    };

    private DialogHost? Host => (owner() as MainWindow)?.Dialogs;

    public Task<bool> ConfirmAsync(string title, string message, string confirmText) =>
        Host is { } host
            ? host.ShowAsync(MessagePanel.Create(title, message, confirmText, cancelText: "Cancel"))
            : MessageWindow.Create(title, message, confirmText, cancelText: "Cancel").ShowDialog<bool>(owner());

    public Task ShowMessageAsync(string title, string message) =>
        Host is { } host
            ? host.ShowAsync(MessagePanel.Create(title, message, "OK", cancelText: null))
            : MessageWindow.Create(title, message, "OK", cancelText: null).ShowDialog<bool>(owner());

    public async Task<IReadOnlyList<string>> PickImportSourcesAsync()
    {
        var files = await owner().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose songs to import",
            AllowMultiple = true,
            FileTypeFilter = [SongFiles, FilePickerFileTypes.All],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        var storage = owner().StorageProvider;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = startFolder is null ? null : await storage.TryGetFolderFromPathAsync(startFolder),
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickExecutableAsync(string? startFolder)
    {
        var storage = owner().StorageProvider;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the program that starts Fuser",
            FileTypeFilter = [Executables, FilePickerFileTypes.All],
            SuggestedStartLocation = startFolder is null ? null : await storage.TryGetFolderFromPathAsync(startFolder),
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public Task<bool> ShowSettingsAsync(SettingsViewModel settings) =>
        (Host ?? throw new InvalidOperationException("Settings are shown over the main window."))
            .ShowAsync(new SettingsPanel { DataContext = settings });
}
