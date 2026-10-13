using System.Collections.Concurrent;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Core.Tests;

internal sealed class FakeScanner : ILibraryScanner
{
    public List<PakFile> Paks { get; } = [];
    public List<PakFile> Duplicates { get; } = [];

    public LibraryScan Scan(LibraryPaths paths) => new(Paks.ToList(), Duplicates.ToList());

    public PakFile Add(string fileName, SongLocation location = SongLocation.Enabled, long size = 1000, string? subfolder = null, bool hasSignature = true)
    {
        var relative = subfolder is null ? $"{fileName}.pak" : Path.Combine(subfolder, $"{fileName}.pak");
        var pak = new PakFile(fileName, Path.Combine(@"C:\Fuser", location.ToString(), relative), relative, location, size,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), hasSignature);
        Paks.Add(pak);
        return pak;
    }

    public void Replace(PakFile old, PakFile updated) => Paks[Paks.IndexOf(old)] = updated;
}

internal sealed class FakeReader : ISongMetadataReader
{
    public ConcurrentBag<string> ReadPaths { get; } = [];
    public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Throwing { get; } = new(StringComparer.OrdinalIgnoreCase);
    public TimeSpan Delay { get; set; }

    public SongReadResult Read(string pakPath)
    {
        ReadPaths.Add(pakPath);
        if (Delay > TimeSpan.Zero)
            Thread.Sleep(Delay);
        var name = Path.GetFileNameWithoutExtension(pakPath);
        if (Throwing.Contains(name))
            throw new InvalidOperationException("boom");
        if (Failing.Contains(name))
            return SongReadResult.Failed("unreadable");
        return new SongReadResult(new SongMetadata($"Title of {name}", "Artist", 2020, Genre.Pop, MusicalKey.C, SongMode.Major, 120), ReadStatus.Ok, null);
    }
}

internal sealed class InMemoryRepository : ISongRepository
{
    public Dictionary<string, Song> Songs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int SaveCalls { get; private set; }

    public Task<IReadOnlyList<Song>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Song>>(Songs.Values.ToList());

    public Task SaveScanResultsAsync(IReadOnlyCollection<Song> songs, CancellationToken ct = default)
    {
        SaveCalls++;
        foreach (var song in songs)
        {
            // Same contract as SQLite: user data of existing rows is never overwritten by a scan.
            Songs[song.FileName] = Songs.TryGetValue(song.FileName, out var existing)
                ? song with { Rating = existing.Rating, Notes = existing.Notes, Author = existing.Author }
                : song;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IReadOnlyCollection<string> fileNames, CancellationToken ct = default)
    {
        foreach (var name in fileNames)
            Songs.Remove(name);
        return Task.CompletedTask;
    }

    public Task UpdateUserDataAsync(string fileName, int rating, string notes, string author, CancellationToken ct = default)
    {
        Songs[fileName] = Songs[fileName] with { Rating = rating, Notes = notes, Author = author };
        return Task.CompletedTask;
    }
}

internal sealed class FakeFileService : ISongFileService
{
    public List<(string FileName, SongLocation Target)> Moves { get; } = [];
    public Dictionary<string, StateChangeOutcome> Outcomes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int CacheClears { get; private set; }

    public StateChangeResult Move(Song song, SongLocation target, LibraryPaths paths)
    {
        Moves.Add((song.FileName, target));
        return new StateChangeResult(song.FileName, Outcomes.GetValueOrDefault(song.FileName, StateChangeOutcome.Moved));
    }

    public void ClearUnlockedSongsCache(LibraryPaths paths) => CacheClears++;
}

internal sealed class FakeGame : IGameProcessService
{
    public bool Running { get; set; }
    public List<AppSettings> Launches { get; } = [];

    public bool IsGameRunning() => Running;

    public LaunchResult Launch(AppSettings settings)
    {
        Launches.Add(settings);
        return new LaunchResult(LaunchOutcome.Started);
    }
}

internal sealed class FakeImporter : IArchiveImporter
{
    public ImportResult Result { get; set; } = new([], []);
    public int Calls { get; private set; }

    public Task<ImportResult> ImportAsync(IReadOnlyList<string> sources, LibraryPaths paths, ImportOptions options,
        IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
