using System.Diagnostics;
using CUE4Parse.UE4.Versions;
using FuserSongManager.Core.Models;
using FuserSongManager.Core.Services;
using FuserSongManager.Infrastructure.Data;
using FuserSongManager.Infrastructure.Files;
using FuserSongManager.Infrastructure.Pak;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;

const string Usage = """
    FuserSongManager.Tools — diagnostic CLI for Fuser custom song paks

    Usage:
      list    <pak>                    List the files inside a pak
      dump    <pak> [--out <file>]     Dump every property of the Meta_* assets as JSON
      read    <pak|dir> [--json]       Read the seven song fields (dir is scanned recursively)
      compare <v1.json> <dir>          Compare 'read' results against a v1 reference dump
      sync    <fuser dir> --db <file>  Incremental library sync into a v2 database
              [--force] [--parallel N] [--v1-db <songs.sqlite>]
      enable  <fuser dir> --db <file> <song_P>...   Move songs to custom_songs
      disable <fuser dir> --db <file> <song_P>...   Move songs to disabled_songs
      import  <fuser dir> --db <file> <archive|pak>... [--disabled] [--no-replace]

    Options:
      --game <EGame>   Engine version for CUE4Parse (default GAME_UE4_24)
      --verbose        Show CUE4Parse log output
    """;

var options = Options.Parse(args);
if (options.Positional.Count == 0)
{
    Console.WriteLine(Usage);
    return 1;
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(options.Verbose ? LogEventLevel.Verbose : LogEventLevel.Error)
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateLogger();

try
{
    return options.Positional[0] switch
    {
        "list" when options.Positional.Count == 2 => List(options.Positional[1]),
        "dump" when options.Positional.Count == 2 => Dump(options.Positional[1]),
        "read" when options.Positional.Count == 2 => Read(options.Positional[1]),
        "compare" when options.Positional.Count == 3 => Compare(options.Positional[1], options.Positional[2]),
        "sync" when options.Positional.Count == 2 && options.Db is not null => await Sync(options.Positional[1], options.Db),
        "enable" or "disable" when options.Positional.Count >= 3 && options.Db is not null =>
            await SetState(options.Positional[1], options.Db, options.Positional[0] == "enable" ? SongLocation.Enabled : SongLocation.Disabled, options.Positional[2..]),
        "import" when options.Positional.Count >= 3 && options.Db is not null => await Import(options.Positional[1], options.Db, options.Positional[2..]),
        _ => Fail(Usage),
    };
}
catch (Exception ex)
{
    return Fail($"{ex.GetType().Name}: {ex.Message}");
}

int List(string pakPath)
{
    using var provider = FuserPak.Open(pakPath, options.Game);
    foreach (var path in provider.Files.Keys.Order(StringComparer.OrdinalIgnoreCase))
        Console.WriteLine(path);
    Console.Error.WriteLine($"{provider.Files.Count} files");
    return 0;
}

int Dump(string pakPath)
{
    using var provider = FuserPak.Open(pakPath, options.Game);
    var shortName = FuserPak.ShortName(pakPath);
    var dump = new JObject();
    foreach (var assetPath in new[] { FuserPak.SongSetAssetPath(shortName), FuserPak.BassCelAssetPath(shortName) })
    {
        try
        {
            var exports = provider.LoadPackage(assetPath).GetExports();
            dump[assetPath] = JToken.FromObject(exports, JsonSerializer.Create());
        }
        catch (Exception ex)
        {
            dump[assetPath] = $"ERROR {ex.GetType().Name}: {ex.Message}";
        }
    }

    var json = dump.ToString(Formatting.Indented);
    if (options.Out is null)
        Console.WriteLine(json);
    else
        File.WriteAllText(options.Out, json);
    return 0;
}

int Read(string target)
{
    var results = ReadAll(target);
    if (options.Json)
    {
        Console.WriteLine(JsonConvert.SerializeObject(results.Select(r => r.Metadata ?? (object)new { r.ShortName, r.Error }), Formatting.Indented));
        return 0;
    }

    foreach (var r in results)
    {
        if (r.Metadata is { } m)
        {
            var status = m.Missing.Count == 0 ? "ok" : $"partial (missing {string.Join(", ", m.Missing)})";
            Console.WriteLine($"{m.ShortName,-36} {m.Title} — {m.Artist} | {m.Year} | {m.Genre} | {m.Key} {m.Mode} | {m.Bpm} BPM | {status}");
        }
        else
        {
            Console.WriteLine($"{r.ShortName,-36} FAILED: {r.Error}");
        }
    }
    Console.Error.WriteLine($"{results.Count} paks, {results.Count(r => r.Metadata is { Missing.Count: 0 })} ok");
    return 0;
}

int Compare(string v1JsonPath, string dir)
{
    var reference = JArray.Parse(File.ReadAllText(v1JsonPath))
        .ToDictionary(t => (string)t["short_name"]!, t => t, StringComparer.OrdinalIgnoreCase);
    var results = ReadAll(dir);
    var mismatches = 0;

    foreach (var r in results)
    {
        if (!reference.TryGetValue(r.ShortName, out var v1))
        {
            Console.WriteLine($"{r.ShortName}: not in v1 reference");
            mismatches++;
            continue;
        }
        if (r.Metadata is not { } m)
        {
            Console.WriteLine($"{r.ShortName}: v2 failed ({r.Error})");
            mismatches++;
            continue;
        }

        var diffs = new List<string>();
        void Check(string field, object? v2Value, JToken? v1Value)
        {
            var a = v2Value?.ToString();
            var b = v1Value?.Type == JTokenType.Null ? null : v1Value?.ToString();
            if (!string.Equals(a, b, StringComparison.Ordinal))
                diffs.Add($"{field}: v1='{b}' v2='{a}'");
        }
        Check("title", m.Title, v1["title"]);
        Check("artist", m.Artist, v1["artist"]);
        Check("year", m.Year, v1["year"]);
        Check("genre", m.Genre, v1["genre"]);
        Check("key", m.Key, v1["key"]);
        Check("mode", m.Mode, v1["mode"]);
        Check("bpm", m.Bpm, v1["bpm"]);

        if (diffs.Count == 0)
        {
            Console.WriteLine($"{r.ShortName}: match");
        }
        else
        {
            mismatches++;
            Console.WriteLine($"{r.ShortName}: MISMATCH");
            foreach (var d in diffs)
                Console.WriteLine($"    {d}");
        }
    }

    Console.WriteLine($"\n{results.Count - mismatches}/{results.Count} match");
    return mismatches == 0 ? 0 : 2;
}

async Task<int> Sync(string fuserDirectory, string databasePath)
{
    var database = new SqliteDatabase(databasePath);
    var created = database.Migrate();
    var repository = new SqliteSongRepository(database);
    Console.WriteLine($"Database {databasePath} ({(created ? "created" : "existing")}, schema v{SqliteDatabase.LatestVersion})");

    if (options.V1Db is not null)
    {
        var v1Songs = V1Import.ReadSongs(options.V1Db);
        await repository.InsertIfMissingAsync(v1Songs);
        Console.WriteLine($"Imported {v1Songs.Count} rows from v1 database {options.V1Db}");
    }

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

    var service = new LibrarySyncService(new LibraryScanner(), new CueSongMetadataReader(PatchedDlcCatalog.LoadEmbedded(), options.Game), repository);
    var syncOptions = new SyncOptions { ForceReread = options.Force, Parallelism = options.Parallelism ?? LibraryPaths.DefaultParallelism };
    var progress = new Progress<SyncProgress>(p => Console.Error.Write($"\r  {p.Processed}/{p.Total} {p.FileName,-50}"));

    var result = await service.SyncAsync(LibraryPaths.FromFuserDirectory(fuserDirectory), syncOptions, progress, cancellation.Token);
    Console.Error.WriteLine();
    Console.WriteLine($"""
        Sync finished in {result.Elapsed.TotalMilliseconds:F0} ms (parallelism {syncOptions.Parallelism})
          added     {result.Added}
          reread    {result.Reread}
          updated   {result.Updated} (moved / .sig changed, not read)
          removed   {result.Removed}
          unchanged {result.Unchanged}
          failed    {result.Failed}
          paks read {result.PaksRead}
        """);
    foreach (var duplicate in result.Duplicates)
        Console.WriteLine($"  duplicate ignored: {duplicate.FullPath}");

    var songs = await repository.GetAllAsync();
    Console.WriteLine($"{songs.Count} songs in database, {songs.Count(s => s.IsEnabled)} enabled, {songs.Count(s => !s.HasSignature)} without .sig, {songs.Count(s => s.ReadStatus != ReadStatus.Ok)} not ok");
    return 0;
}

async Task<int> SetState(string fuserDirectory, string databasePath, SongLocation target, List<string> fileNames)
{
    var database = new SqliteDatabase(databasePath);
    database.Migrate();
    var repository = new SqliteSongRepository(database);
    var songs = (await repository.GetAllAsync()).ToDictionary(s => s.FileName, StringComparer.OrdinalIgnoreCase);

    var selected = new List<Song>();
    foreach (var name in fileNames)
    {
        if (songs.TryGetValue(name, out var song))
            selected.Add(song);
        else
            Console.WriteLine($"{name}: not in the database (run sync first)");
    }

    var service = new SongStateService(new SongFileService(), repository, new GameProcessService());
    var results = await service.SetLocationAsync(selected, target, LibraryPaths.FromFuserDirectory(fuserDirectory));
    foreach (var result in results)
        Console.WriteLine($"{result.FileName}: {result.Outcome}{(result.Message is null ? "" : " — " + result.Message)}");
    return results.All(r => r.Succeeded) && selected.Count == fileNames.Count ? 0 : 2;
}

async Task<int> Import(string fuserDirectory, string databasePath, List<string> sources)
{
    var database = new SqliteDatabase(databasePath);
    database.Migrate();
    var repository = new SqliteSongRepository(database);
    var scanner = new LibraryScanner();
    var sync = new LibrarySyncService(scanner, new CueSongMetadataReader(PatchedDlcCatalog.LoadEmbedded(), options.Game), repository);
    var service = new SongImportService(new SharpCompressImporter(scanner), new GameProcessService(), new SongFileService(), sync);

    var importOptions = new ImportOptions
    {
        Target = options.Disabled ? SongLocation.Disabled : SongLocation.Enabled,
        ReplaceExisting = !options.NoReplace,
    };
    var progress = new Progress<ImportProgress>(p => Console.Error.WriteLine($"  [{p.SourceIndex}/{p.SourceCount}] {p.Stage} {Path.GetFileName(p.Source)}"));
    var stopwatch = Stopwatch.StartNew();
    var (import, syncResult) = await service.ImportAsync(sources, LibraryPaths.FromFuserDirectory(fuserDirectory), importOptions, progress);

    Console.WriteLine($"Import finished in {stopwatch.ElapsedMilliseconds} ms: {import.Imported.Count} installed, {import.Issues.Count} issues");
    foreach (var song in import.Imported)
        Console.WriteLine($"  {(song.Replaced ? "replaced" : "added   ")} {song.FileName} → {song.Location}/{song.RelativePath}");
    foreach (var issue in import.Issues)
        Console.WriteLine($"  {issue.Kind,-18} {Path.GetFileName(issue.Source)}: {issue.Message}");
    if (syncResult is not null)
        Console.WriteLine($"Sync: {syncResult.Added} added, {syncResult.Reread} reread, {syncResult.Failed} failed");
    return 0;
}

List<ReadResult> ReadAll(string target)
{
    var paks = Directory.Exists(target)
        ? Directory.EnumerateFiles(target, "*.pak", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList()
        : [target];

    var stopwatch = Stopwatch.StartNew();
    var results = paks.Select(pak =>
    {
        var shortName = FuserPak.ShortName(pak);
        try
        {
            return new ReadResult(shortName, PakSongMetadataExtractor.Read(pak, options.Game), null);
        }
        catch (Exception ex)
        {
            return new ReadResult(shortName, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }).ToList();
    Console.Error.WriteLine($"Read {results.Count} paks in {stopwatch.ElapsedMilliseconds} ms");
    return results;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

record ReadResult(string ShortName, PakSongMetadata? Metadata, string? Error);

sealed class Options
{
    public List<string> Positional { get; } = [];
    public EGame Game { get; private set; } = FuserPak.DefaultGame;
    public string? Out { get; private set; }
    public bool Json { get; private set; }
    public bool Verbose { get; private set; }
    public string? Db { get; private set; }
    public string? V1Db { get; private set; }
    public bool Force { get; private set; }
    public int? Parallelism { get; private set; }
    public bool Disabled { get; private set; }
    public bool NoReplace { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game": options.Game = Enum.Parse<EGame>(args[++i], ignoreCase: true); break;
                case "--out": options.Out = args[++i]; break;
                case "--json": options.Json = true; break;
                case "--verbose": options.Verbose = true; break;
                case "--db": options.Db = args[++i]; break;
                case "--v1-db": options.V1Db = args[++i]; break;
                case "--force": options.Force = true; break;
                case "--disabled": options.Disabled = true; break;
                case "--no-replace": options.NoReplace = true; break;
                case "--parallel": options.Parallelism = int.Parse(args[++i]); break;
                default: options.Positional.Add(args[i]); break;
            }
        }
        return options;
    }
}
