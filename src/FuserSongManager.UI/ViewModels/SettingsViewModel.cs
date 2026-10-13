using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;
using FuserSongManager.UI.Services;

namespace FuserSongManager.UI.ViewModels;

/// <summary>Fuser folder, launcher and executable (RF-CFG-01, 03, 04). Edits a copy; nothing is saved until the caller applies <see cref="ToSettings"/>.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    public SettingsViewModel(AppSettings settings, IDialogService dialogs)
    {
        _dialogs = dialogs;
        FuserDirectory = settings.FuserDirectory ?? "";
        LauncherType = settings.LauncherType;
        ExecutablePath = settings.ExecutablePath ?? "";
    }

    public IReadOnlyList<LauncherType> LauncherTypes { get; } = Enum.GetValues<LauncherType>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderError), nameof(FolderInfo), nameof(CanSave))]
    public partial string FuserDirectory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExecutableNeeded), nameof(LauncherNote), nameof(ExecutableError), nameof(CanSave))]
    public partial LauncherType LauncherType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExecutableError), nameof(CanSave))]
    public partial string ExecutablePath { get; set; }

    /// <summary>The install folder found from what the user typed or picked (it may be a folder inside the install).</summary>
    public string? ResolvedFuserDirectory => FuserInstall.FindInstallFolder(FuserDirectory);

    public string? FolderError =>
        string.IsNullOrWhiteSpace(FuserDirectory) ? "Choose the folder where Fuser is installed."
        : ResolvedFuserDirectory is null ? "This does not look like a Fuser install: the folder Fuser\\Content\\Paks was not found inside it."
        : null;

    public string? FolderInfo =>
        ResolvedFuserDirectory is { } resolved
            ? $"Songs folder: {LibraryPaths.FromFuserDirectory(resolved).EnabledDirectory}"
            : null;

    public bool IsExecutableNeeded => LauncherType == LauncherType.Local;

    public string? LauncherNote => LauncherType switch
    {
        LauncherType.Steam => "Fuser is started through Steam.",
        LauncherType.Epic => "Starting Fuser from the manager is not supported with Epic. Start it from the Epic Games Launcher.",
        _ => "Fuser is started with the executable or script below.",
    };

    public string? ExecutableError =>
        !IsExecutableNeeded ? null
        : string.IsNullOrWhiteSpace(ExecutablePath) ? "Choose the executable or script that starts Fuser."
        : !File.Exists(ExecutablePath) ? "The file was not found."
        : null;

    public bool CanSave => FolderError is null && ExecutableError is null;

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var folder = await _dialogs.PickFolderAsync("Choose the Fuser install folder", Directory.Exists(FuserDirectory) ? FuserDirectory : null);
        if (folder is not null)
            FuserDirectory = FuserInstall.FindInstallFolder(folder) ?? folder;
    }

    [RelayCommand]
    private async Task BrowseExecutable()
    {
        var start = Path.GetDirectoryName(ExecutablePath);
        var file = await _dialogs.PickExecutableAsync(Directory.Exists(start) ? start : ResolvedFuserDirectory);
        if (file is not null)
            ExecutablePath = file;
    }

    /// <summary>Folder of the log files; the button to open it is shown only when set (RF-SIS-01).</summary>
    public string? LogsDirectory { get; init; }

    public IShellService? Shell { get; init; }

    public bool CanOpenLogs => LogsDirectory is not null && Shell is not null;

    [RelayCommand]
    private void OpenLogs()
    {
        if (LogsDirectory is null || Shell is null)
            return;
        Directory.CreateDirectory(LogsDirectory);
        Shell.OpenFolder(LogsDirectory);
    }

    public AppSettings ToSettings(AppSettings current) => current with
    {
        FuserDirectory = ResolvedFuserDirectory ?? FuserDirectory.Trim(),
        LauncherType = LauncherType,
        ExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath.Trim(),
    };
}
