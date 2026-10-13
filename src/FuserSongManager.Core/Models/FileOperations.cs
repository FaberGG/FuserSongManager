namespace FuserSongManager.Core.Models;

public enum StateChangeOutcome
{
    Moved,
    AlreadyThere,
    /// <summary>Enabling requires the .sig next to the .pak (RF-EST-04).</summary>
    MissingSignature,
    /// <summary>A file with the same name exists in the destination; nothing was overwritten (RF-EST-06).</summary>
    DestinationExists,
    /// <summary>The song has no known path yet (imported from v1 and not scanned).</summary>
    NotIndexed,
    GameRunning,
    Failed,
}

public sealed record StateChangeResult(string FileName, StateChangeOutcome Outcome, string? Message = null)
{
    public bool Succeeded => Outcome is StateChangeOutcome.Moved or StateChangeOutcome.AlreadyThere;
}

public sealed record ImportOptions
{
    /// <summary>Where new songs go (RF-IMP-04). Songs already installed stay where they are (RF-IMP-05).</summary>
    public SongLocation Target { get; init; } = SongLocation.Enabled;

    /// <summary>Overwrite the files of a song that is already installed, as v1 did. If false it is skipped.</summary>
    public bool ReplaceExisting { get; init; } = true;
}

public enum ImportIssueKind
{
    /// <summary>A .pak without its .sig (RF-IMP-03).</summary>
    MissingSignature,
    AlreadyInstalled,
    DuplicateInImport,
    /// <summary>The archive holds several files with the same song name; it is unclear which to use.</summary>
    Ambiguous,
    NoSongsFound,
    UnsupportedFile,
    UnreadableArchive,
    GameRunning,
    Failed,
}

/// <param name="Source">Archive or file the issue comes from.</param>
/// <param name="FileName">Song pak name (<c>xxx_P</c>) when the issue concerns one song.</param>
public sealed record ImportIssue(string Source, string? FileName, ImportIssueKind Kind, string Message);

public sealed record ImportedSong(string FileName, SongLocation Location, string RelativePath, bool Replaced);

public sealed record ImportResult(IReadOnlyList<ImportedSong> Imported, IReadOnlyList<ImportIssue> Issues)
{
    public static ImportResult Blocked(ImportIssue issue) => new([], [issue]);
}

public sealed record ImportProgress(int SourceIndex, int SourceCount, string Source, string Stage);

public enum LaunchOutcome
{
    Started,
    AlreadyRunning,
    EpicNotSupported,
    ExecutableMissing,
    NotConfigured,
    Failed,
}

public sealed record LaunchResult(LaunchOutcome Outcome, string? Message = null);
