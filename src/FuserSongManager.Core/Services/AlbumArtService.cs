using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Services;

/// <summary>
/// Album art with a disk cache, so selecting a song again does not reopen its pak (RF-PEN-14).
/// Entries are keyed by the pak's size and date: replacing the pak invalidates its art.
/// Paks without art are remembered too, with an empty marker file.
/// </summary>
public sealed class AlbumArtService(ISongMediaReader reader, string? cacheDirectory)
{
    private const string NoArtExtension = ".none";

    /// <summary>The art as PNG, or null if the pak has none. Never throws for a bad pak or cache.</summary>
    public byte[]? Get(Song song, string pakPath)
    {
        var key = CacheKey(song);
        var cached = cacheDirectory is null || key is null ? null : Path.Combine(cacheDirectory, key);

        if (cached is not null)
        {
            try
            {
                if (File.Exists(cached + ".png"))
                    return File.ReadAllBytes(cached + ".png");
                if (File.Exists(cached + NoArtExtension))
                    return null;
            }
            catch (IOException)
            {
                // Unreadable cache entry: read the pak again.
            }
        }

        byte[]? art;
        try
        {
            art = reader.ReadAlbumArt(pakPath);
        }
        catch (Exception)
        {
            // An unreadable pak is already flagged in the list; it just shows no art. Not cached, so it is retried.
            return null;
        }

        if (cached is not null)
            Store(song.FileName, cached, art);
        return art;
    }

    private void Store(string fileName, string cached, byte[]? art)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory!);
            // Drop entries of older versions of the same pak.
            foreach (var old in Directory.EnumerateFiles(cacheDirectory!, Prefix(fileName) + "*"))
                File.Delete(old);

            if (art is null)
            {
                File.WriteAllBytes(cached + NoArtExtension, []);
            }
            else
            {
                var temp = cached + ".tmp";
                File.WriteAllBytes(temp, art);
                File.Move(temp, cached + ".png", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is only an optimisation.
        }
    }

    private static string Prefix(string fileName) => fileName.ToLowerInvariant() + "~";

    /// <summary>Null until the song has been scanned by v2 (no size or date to key the entry on).</summary>
    private static string? CacheKey(Song song) =>
        song.FileSize is { } size && song.FileModifiedUtc is { } modified
            ? $"{Prefix(song.FileName)}{size}-{modified.Ticks}"
            : null;
}
