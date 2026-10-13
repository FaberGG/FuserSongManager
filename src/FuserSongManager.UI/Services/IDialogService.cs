using FuserSongManager.UI.ViewModels;

namespace FuserSongManager.UI.Services;

/// <summary>Windows and pickers the view models need, kept behind an interface so they can be tested without a UI.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    Task ShowMessageAsync(string title, string message);

    /// <summary>Archives (.zip, .rar, .7z) or loose .pak files. Empty if cancelled.</summary>
    Task<IReadOnlyList<string>> PickImportSourcesAsync();

    Task<string?> PickFolderAsync(string title, string? startFolder);

    Task<string?> PickExecutableAsync(string? startFolder);

    /// <summary>True if the user saved.</summary>
    Task<bool> ShowSettingsAsync(SettingsViewModel settings);
}

/// <summary>Operating system integration.</summary>
public interface IShellService
{
    /// <summary>Opens the file manager with the file selected (RF-DET-05).</summary>
    void ShowInFileManager(string path);

    void OpenFolder(string folder);
}
