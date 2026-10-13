namespace FuserSongManager.Core.Services;

/// <summary>Recognises a Fuser install folder (RF-CFG-01, RF-CFG-03).</summary>
public static class FuserInstall
{
    /// <summary>Default Steam location, offered on first run when it exists.</summary>
    public static readonly string[] CommonLocations =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\Fuser",
        @"C:\Program Files\Epic Games\Fuser",
    ];

    /// <summary>True when <c>Fuser/Content/Paks</c> exists under the folder.</summary>
    public static bool IsInstallFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Directory.Exists(Path.Combine(folder, "Fuser", "Content", "Paks"));

    /// <summary>
    /// Returns the install folder for <paramref name="folder"/>, which may be the install folder itself
    /// or any folder inside it (users often pick <c>Fuser\Content\Paks</c> or <c>custom_songs</c>).
    /// Null if no install folder is found.
    /// </summary>
    public static string? FindInstallFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        for (var current = new DirectoryInfo(Path.GetFullPath(folder.Trim())); current is not null; current = current.Parent)
        {
            if (IsInstallFolder(current.FullName))
                return current.FullName;
        }
        return null;
    }

    public static string? FindCommonLocation() => CommonLocations.FirstOrDefault(IsInstallFolder);
}
