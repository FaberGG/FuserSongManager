namespace FuserSongManager.Core.Models;

/// <summary>A song pak found on disk.</summary>
/// <param name="FileName">Pak name without extension (<c>xxx_P</c>); the library key, as in v1.</param>
/// <param name="FullPath">Absolute path of the <c>.pak</c>.</param>
/// <param name="RelativePath">Path relative to the enabled or disabled songs folder, keeping subfolders.</param>
public sealed record PakFile(
    string FileName,
    string FullPath,
    string RelativePath,
    SongLocation Location,
    long Size,
    DateTime ModifiedUtc,
    bool HasSignature);

/// <summary>Result of enumerating both song folders once.</summary>
/// <param name="Duplicates">Paks whose name already appeared in another folder; only the first one is kept.</param>
public sealed record LibraryScan(IReadOnlyList<PakFile> Paks, IReadOnlyList<PakFile> Duplicates);
