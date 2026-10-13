using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace FuserSongManager.Infrastructure.Files;

/// <summary>
/// Installs songs from .zip/.rar/.7z archives (any folder depth, several songs per archive) or from loose
/// .pak files. Only complete .pak + .sig pairs are installed. Extraction goes to a private temp folder that
/// is always deleted afterwards (RF-IMP-09). Destination paths are built from file names only, never from
/// archive paths, so a malicious archive cannot write outside the songs folders.
/// </summary>
public sealed class SharpCompressImporter(ILibraryScanner scanner, string? tempRoot = null) : IArchiveImporter
{
    private const string StagingSuffix = ".fsmtmp";
    private const string BackupSuffix = ".fsmbak";

    public Task<ImportResult> ImportAsync(
        IReadOnlyList<string> sources,
        LibraryPaths paths,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default) =>
        Task.Run(() => Import(sources, paths, options, progress, ct), ct);

    private ImportResult Import(IReadOnlyList<string> sources, LibraryPaths paths, ImportOptions options, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var workDirectory = Path.Combine(tempRoot ?? Path.Combine(Path.GetTempPath(), "FuserSongManager"), "import-" + Guid.NewGuid().ToString("N"));
        var imported = new List<ImportedSong>();
        var issues = new List<ImportIssue>();
        var installedNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var existing = scanner.Scan(paths).Paks.ToDictionary(p => p.FileName, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < sources.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var source = sources[i];
                progress?.Report(new ImportProgress(i, sources.Count, source, "Reading"));

                var candidates = Path.GetExtension(source).Equals(".pak", StringComparison.OrdinalIgnoreCase)
                    ? FromLoosePak(source, issues)
                    : FromArchive(source, Path.Combine(workDirectory, i.ToString()), issues, ct);

                progress?.Report(new ImportProgress(i, sources.Count, source, "Installing"));
                foreach (var candidate in candidates)
                    Install(candidate, existing, installedNow, paths, options, imported, issues);
            }

            progress?.Report(new ImportProgress(sources.Count, sources.Count, "", "Done"));
            return new ImportResult(imported, issues);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private static List<Candidate> FromLoosePak(string pakPath, List<ImportIssue> issues)
    {
        var name = Path.GetFileNameWithoutExtension(pakPath);
        if (!File.Exists(pakPath))
        {
            issues.Add(new ImportIssue(pakPath, name, ImportIssueKind.Failed, $"{pakPath} does not exist."));
            return [];
        }
        var sigPath = Path.ChangeExtension(pakPath, ".sig");
        if (!File.Exists(sigPath))
        {
            issues.Add(new ImportIssue(pakPath, name, ImportIssueKind.MissingSignature, $"{Path.GetFileName(sigPath)} is missing next to the .pak; the song was not added."));
            return [];
        }
        return [new Candidate(pakPath, name, pakPath, sigPath)];
    }

    private static List<Candidate> FromArchive(string archivePath, string extractDirectory, List<ImportIssue> issues, CancellationToken ct)
    {
        IArchive archive;
        try
        {
            archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
        }
        catch (Exception ex)
        {
            issues.Add(new ImportIssue(archivePath, null, ImportIssueKind.UnreadableArchive, $"Could not open {Path.GetFileName(archivePath)} as an archive: {ex.Message}"));
            return [];
        }

        using (archive)
        {
            try
            {
                return ExtractPairs(archive, archivePath, extractDirectory, issues, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                issues.Add(new ImportIssue(archivePath, null, ImportIssueKind.UnreadableArchive, $"Could not extract {Path.GetFileName(archivePath)}: {ex.Message}"));
                return [];
            }
        }
    }

    private static List<Candidate> ExtractPairs(IArchive archive, string archivePath, string extractDirectory, List<ImportIssue> issues, CancellationToken ct)
    {
        var files = archive.Entries.Where(e => !e.IsDirectory && e.Key is not null && !IsJunk(e.Key)).ToList();
        ILookup<string, IArchiveEntry> ByExtension(string extension) => files
            .Where(e => Path.GetExtension(e.Key!).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .ToLookup(e => Path.GetFileNameWithoutExtension(e.Key!), StringComparer.OrdinalIgnoreCase);
        var paks = ByExtension(".pak");
        var sigs = ByExtension(".sig");

        if (paks.Count == 0)
        {
            issues.Add(new ImportIssue(archivePath, null, ImportIssueKind.NoSongsFound, $"{Path.GetFileName(archivePath)} does not contain any .pak file."));
            return [];
        }

        // Archive entry key → where to extract it. Pairs are matched by file name, wherever they are in the archive (as v1).
        var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidates = new List<Candidate>();
        foreach (var group in paks)
        {
            var name = group.Key;
            var matchingSigs = sigs[name].ToList();
            if (group.Count() > 1 || matchingSigs.Count > 1)
            {
                issues.Add(new ImportIssue(archivePath, name, ImportIssueKind.Ambiguous, $"The archive contains several copies of {name}; it was skipped. Extract it by hand and add the right .pak."));
                continue;
            }
            if (matchingSigs.Count == 0)
            {
                issues.Add(new ImportIssue(archivePath, name, ImportIssueKind.MissingSignature, $"{name}.sig is missing in the archive; the song was not added."));
                continue;
            }
            var pakEntry = group.Single();
            var sigEntry = matchingSigs[0];
            if (pakEntry.IsEncrypted || sigEntry.IsEncrypted)
            {
                issues.Add(new ImportIssue(archivePath, name, ImportIssueKind.UnreadableArchive, $"{name} is password-protected in the archive."));
                continue;
            }

            var folder = Path.Combine(extractDirectory, candidates.Count.ToString());
            var candidate = new Candidate(archivePath, name, Path.Combine(folder, name + ".pak"), Path.Combine(folder, name + ".sig"));
            wanted[pakEntry.Key!] = candidate.PakPath;
            wanted[sigEntry.Key!] = candidate.SigPath;
            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
            return [];

        if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
        {
            // Solid archives must be read front to back; SharpCompress only allows the sequential reader here.
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.Entry.Key is { } key && wanted.TryGetValue(key, out var destination))
                {
                    using var output = CreateFile(destination);
                    reader.WriteEntryTo(output);
                }
            }
        }
        else
        {
            foreach (var entry in files.Where(e => wanted.ContainsKey(e.Key!)))
            {
                ct.ThrowIfCancellationRequested();
                using var input = entry.OpenEntryStream();
                using var output = CreateFile(wanted[entry.Key!]);
                input.CopyTo(output);
            }
        }

        var extracted = candidates.Where(c => File.Exists(c.PakPath) && File.Exists(c.SigPath)).ToList();
        foreach (var missing in candidates.Except(extracted))
            issues.Add(new ImportIssue(archivePath, missing.FileName, ImportIssueKind.Failed, $"{missing.FileName} could not be extracted from the archive."));
        return extracted;
    }

    private static void Install(
        Candidate candidate,
        Dictionary<string, PakFile> existing,
        HashSet<string> installedNow,
        LibraryPaths paths,
        ImportOptions options,
        List<ImportedSong> imported,
        List<ImportIssue> issues)
    {
        if (!installedNow.Add(candidate.FileName))
        {
            issues.Add(new ImportIssue(candidate.Source, candidate.FileName, ImportIssueKind.DuplicateInImport, $"{candidate.FileName} was already added from another file in this import; this copy was skipped."));
            return;
        }

        string targetPak;
        ImportedSong song;
        if (existing.TryGetValue(candidate.FileName, out var installed))
        {
            if (Path.GetFullPath(installed.FullPath).Equals(Path.GetFullPath(candidate.PakPath), StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ImportIssue(candidate.Source, candidate.FileName, ImportIssueKind.AlreadyInstalled, $"{candidate.FileName} is already in the library."));
                return;
            }
            if (!options.ReplaceExisting)
            {
                issues.Add(new ImportIssue(candidate.Source, candidate.FileName, ImportIssueKind.AlreadyInstalled, $"{candidate.FileName} is already installed; it was not replaced."));
                return;
            }
            // Replace in place: keep its folder and enabled/disabled state, never create a copy in the other folder (RF-IMP-05).
            targetPak = installed.FullPath;
            song = new ImportedSong(candidate.FileName, installed.Location, installed.RelativePath, Replaced: true);
        }
        else
        {
            targetPak = Path.Combine(paths.RootOf(options.Target), candidate.FileName + ".pak");
            song = new ImportedSong(candidate.FileName, options.Target, candidate.FileName + ".pak", Replaced: false);
        }

        try
        {
            CommitPair(candidate.PakPath, candidate.SigPath, targetPak, Path.ChangeExtension(targetPak, ".sig"));
            imported.Add(song);
        }
        catch (Exception ex)
        {
            issues.Add(new ImportIssue(candidate.Source, candidate.FileName, ImportIssueKind.Failed, $"Could not install {candidate.FileName}: {ex.Message}. Nothing was changed."));
        }
    }

    /// <summary>
    /// Copies both files next to their destination first, then swaps them in. On any failure the previous
    /// files (if any) are restored, so the destination never ends with a .pak and .sig from different versions.
    /// </summary>
    private static void CommitPair(string sourcePak, string sourceSig, string targetPak, string targetSig)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPak)!);
        var files = new[] { (Source: sourcePak, Target: targetPak), (Source: sourceSig, Target: targetSig) }
            .Select(f => new StagedFile(f.Source, f.Target))
            .ToArray();

        try
        {
            foreach (var file in files)
                File.Copy(file.Source, file.Staging, overwrite: true);
            foreach (var file in files)
            {
                if (File.Exists(file.Target))
                {
                    File.Move(file.Target, file.Backup, overwrite: true);
                    file.BackedUp = true;
                }
            }
            foreach (var file in files)
            {
                File.Move(file.Staging, file.Target);
                file.Committed = true;
            }
        }
        catch
        {
            foreach (var file in files)
            {
                TryDo(() =>
                {
                    if (file.Committed)
                        File.Delete(file.Target);
                    if (file.BackedUp)
                        File.Move(file.Backup, file.Target, overwrite: true);
                });
                TryDo(() => File.Delete(file.Staging));
            }
            throw;
        }

        foreach (var file in files)
            TryDo(() => File.Delete(file.Backup));
    }

    private static FileStream CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.Create(path);
    }

    private static bool IsJunk(string key) =>
        key.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(key).StartsWith("._", StringComparison.Ordinal);

    private static void TryDeleteDirectory(string directory) => TryDo(() =>
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    });

    private static void TryDo(Action action)
    {
        try
        {
            action();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record Candidate(string Source, string FileName, string PakPath, string SigPath);

    private sealed class StagedFile(string source, string target)
    {
        public string Source { get; } = source;
        public string Target { get; } = target;
        public string Staging { get; } = target + StagingSuffix;
        public string Backup { get; } = target + BackupSuffix;
        public bool BackedUp { get; set; }
        public bool Committed { get; set; }
    }
}
