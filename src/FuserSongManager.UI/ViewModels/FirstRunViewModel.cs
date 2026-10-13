using CommunityToolkit.Mvvm.ComponentModel;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;
using FuserSongManager.UI.Services;

namespace FuserSongManager.UI.ViewModels;

/// <summary>What the first-run wizard found from a previous install of the v1 manager.</summary>
/// <param name="Songs">Rows of the v1 database; their paks are re-read by the first scan.</param>
public sealed record V1Library(string DatabasePath, AppSettings? Settings, IReadOnlyList<Song> Songs);

/// <summary>
/// First run (RF-CFG-02): choose the Fuser folder, prefilled from v1 or the default Steam location,
/// and optionally bring the v1 library (ratings, notes and authors, RF-USR-05).
/// </summary>
public sealed partial class FirstRunViewModel : SettingsViewModel
{
    public FirstRunViewModel(V1Library? v1, IDialogService dialogs)
        : base(InitialSettings(v1), dialogs)
    {
        V1 = v1;
        ImportV1 = v1 is { Songs.Count: > 0 };
    }

    private static AppSettings InitialSettings(V1Library? v1)
    {
        var settings = v1?.Settings ?? new AppSettings();
        return FuserInstall.IsInstallFolder(settings.FuserDirectory)
            ? settings
            : settings with { FuserDirectory = FuserInstall.FindCommonLocation() ?? settings.FuserDirectory };
    }

    public V1Library? V1 { get; }

    public bool HasV1 => V1 is { Songs.Count: > 0 };

    public string? V1Summary => V1 is null ? null
        : $"Found the library of Fuser Custom Song Manager v1 with {Display.Songs(V1.Songs.Count)} " +
          $"({V1.Songs.Count(s => s.Rating > 0)} rated, {V1.Songs.Count(s => s.Notes.Length > 0)} with notes). " +
          "Your v1 files are only read, never changed.";

    [ObservableProperty]
    public partial bool ImportV1 { get; set; }

    public string Intro =>
        V1?.Settings?.FuserDirectory is { } v1Folder && FuserInstall.IsInstallFolder(v1Folder)
            ? "The Fuser folder was taken from your v1 settings. Check it and continue."
            : "Choose the folder where Fuser is installed (the one that contains the Fuser folder and Fuser.exe).";
}
