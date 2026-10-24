using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Versions;

namespace FuserSongManager.Infrastructure.Pak;

/// <summary>
/// Mounts a single custom song pak and resolves the paths of its metadata assets.
/// </summary>
public static class FuserPak
{
    // Fuser song paks are pak version 8 (UE 4.22–4.24). 4.22 misreads FText; 4.23+ reads it correctly.
    public const EGame DefaultGame = EGame.GAME_UE4_24;

    private static int _registered;

    public static StreamedFileProvider Open(string pakPath, EGame game = DefaultGame)
    {
        RegisterTypes();
        var provider = new StreamedFileProvider("Fuser", new VersionContainer(game), StringComparer.OrdinalIgnoreCase);
        provider.Initialize();
        provider.RegisterVfs(pakPath);
        if (provider.Mount() == 0)
        {
            provider.Dispose();
            throw new InvalidDataException("The file is not a valid or readable pak (it could not be mounted).");
        }
        return provider;
    }

    /// <summary>Short song name used inside the pak: <c>AW_Run_ShpQn_P.pak</c> → <c>AW_Run_ShpQn</c>.</summary>
    public static string ShortName(string pakPath)
    {
        var name = Path.GetFileNameWithoutExtension(pakPath);
        return name.EndsWith("_P", StringComparison.OrdinalIgnoreCase) ? name[..^2] : name;
    }

    /// <summary>
    /// Song name used inside the pak. Normally the file name without <c>_P</c>, but the game loads a pak
    /// whatever it is called, so if the file was renamed the name is taken from the song asset it contains.
    /// </summary>
    public static string ResolveSongName(IFileProvider provider, string pakPath)
    {
        var fromFileName = ShortName(pakPath);
        if (provider.TryGetGameFile(SongSetAssetPath(fromFileName), out _))
            return fromFileName;

        var inPak = provider.Files.Keys
            .Select(path => SongSetPattern.Match(path))
            .Where(m => m.Success && m.Groups[1].Value.Equals(m.Groups[2].Value, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return inPak.Count == 1 ? inPak[0] : fromFileName;
    }

    private static readonly Regex SongSetPattern = new(
        "^Fuser/Content/DLC/Songs/([^/]+)/Meta_([^/]+)\\.uasset$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Asset with artist, genre and year (<c>CelSongSet</c>).</summary>
    public static string SongSetAssetPath(string shortName) =>
        $"Fuser/Content/DLC/Songs/{shortName}/Meta_{shortName}.uasset";

    /// <summary>Bass cel asset with title, key, mode and BPM (<c>SongCelData</c>).</summary>
    public static string BassCelAssetPath(string shortName) => CelAssetPath(shortName, "bs");

    /// <summary>The <c>SongCelData</c> of one part: <c>bt</c> (beat), <c>bs</c> (bass), <c>lp</c> (loop) or <c>ld</c> (lead).</summary>
    public static string CelAssetPath(string shortName, string cel) =>
        $"Fuser/Content/Audio/Songs/{shortName}/{shortName}{cel}/Meta_{shortName}{cel}.uasset";

    public static UObject LoadFirstExport(IFileProvider provider, string assetPath)
    {
        if (!provider.TryGetGameFile(assetPath, out _))
            throw new FileNotFoundException($"The pak does not contain the song metadata asset {assetPath}. The pak name may not match the song inside it.");
        return provider.LoadPackage(assetPath).GetExports().First();
    }

    private static void RegisterTypes()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;
        ObjectTypeRegistry.RegisterClass(nameof(CelSongSet), typeof(CelSongSet));
        ObjectTypeRegistry.RegisterClass(nameof(SongCelData), typeof(SongCelData));
    }
}
