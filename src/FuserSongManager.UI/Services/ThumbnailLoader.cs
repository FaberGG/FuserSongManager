using Avalonia.Media.Imaging;
using FuserSongManager.UI.ViewModels;

namespace FuserSongManager.UI.Services;

/// <summary>
/// Loads the album art of tiles as they come into view, a few at a time. The art comes from the disk cache
/// when it is there; otherwise the pak is opened, which takes tens of milliseconds, so it never runs on the UI thread.
/// </summary>
public sealed class ThumbnailLoader(AppServices services)
{
    /// <summary>Decoded width: tiles are at most about 220 px, so this stays sharp on 125 % displays.</summary>
    public const int Width = 288;

    private readonly SemaphoreSlim _gate = new(2);
    private readonly List<Task> _pending = [];

    /// <summary>Completes when every requested thumbnail has loaded; for tests.</summary>
    public Task Idle
    {
        get
        {
            lock (_pending)
                return Task.WhenAll(_pending.ToArray());
        }
    }

    public void Request(SongRowViewModel row)
    {
        if (row.ThumbnailRequested)
            return;
        row.ThumbnailRequested = true;
        var task = LoadAsync(row);
        lock (_pending)
        {
            _pending.RemoveAll(t => t.IsCompleted);
            _pending.Add(task);
        }
    }

    private async Task LoadAsync(SongRowViewModel row)
    {
        var song = row.Song;
        if (services.AlbumArt is not { } art || services.FullPathOf(song) is not { } path)
        {
            row.SetThumbnail(null);
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var bitmap = await Task.Run(() =>
            {
                var png = art.Get(song, path);
                return png is null ? null : Bitmap.DecodeToWidth(new MemoryStream(png), Width, BitmapInterpolationMode.HighQuality);
            });
            // The pak may have been replaced while this one was loading; that load shows the new art.
            if (ReferenceEquals(row.Song, song) || row.Song.FileSize == song.FileSize)
                row.SetThumbnail(bitmap);
        }
        catch (Exception ex)
        {
            services.Log.Warning(ex, "Album art of {FileName} could not be shown on its tile", song.FileName);
            row.SetThumbnail(null);
        }
        finally
        {
            _gate.Release();
        }
    }
}
