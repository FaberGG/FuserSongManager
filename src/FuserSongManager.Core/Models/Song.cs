namespace FuserSongManager.Core.Models;

/// <summary>A song as stored in the library: game metadata, file state and the user's own data.</summary>
public sealed record Song
{
    /// <summary>Pak name without extension (<c>xxx_P</c>); primary key.</summary>
    public required string FileName { get; init; }

    public SongMetadata Metadata { get; init; } = SongMetadata.Empty;
    public ReadStatus ReadStatus { get; init; } = ReadStatus.Ok;
    public string? ReadError { get; init; }

    public SongLocation Location { get; init; }

    /// <summary>Path relative to the songs folder of <see cref="Location"/>. Null until the first scan (rows imported from v1).</summary>
    public string? RelativePath { get; init; }

    public bool HasSignature { get; init; }

    /// <summary>File size and timestamp at the last read. Null means the pak was never read by v2.</summary>
    public long? FileSize { get; init; }

    public DateTime? FileModifiedUtc { get; init; }

    /// <summary>0–5 stars.</summary>
    public int Rating { get; init; }

    public string Notes { get; init; } = "";
    public string Author { get; init; } = "";

    public bool IsEnabled => Location == SongLocation.Enabled;
}
