namespace FuserSongManager.Core.Services;

/// <summary>Sorting rules for song lists (RF-BUS-05).</summary>
public static class SongSorting
{
    private static readonly string[] IgnoredPrefixes = ["The ", "A "];

    /// <summary>
    /// The text used to sort a title or artist: without a leading "The " or "A ", like the game does,
    /// so "The Killers" sorts under K. Empty for null so missing values group together.
    /// </summary>
    public static string SortKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim();
        foreach (var prefix in IgnoredPrefixes)
        {
            // Keep the text if it is only the prefix word ("A" alone is a valid title).
            if (trimmed.Length > prefix.Length && trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].TrimStart();
        }
        return trimmed;
    }
}
