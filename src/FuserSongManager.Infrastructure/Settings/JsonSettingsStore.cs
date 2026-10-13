using System.Text.Json;
using System.Text.Json.Serialization;
using FuserSongManager.Core.Abstractions;
using FuserSongManager.Core.Models;

namespace FuserSongManager.Infrastructure.Settings;

/// <summary>Where the manager keeps its own files (separate from v1's <c>FuserCustomSongManager</c> folder).</summary>
public sealed record AppDataPaths(string Root)
{
    public static AppDataPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuserSongManager"));

    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string DatabaseFile => Path.Combine(Root, "songs.sqlite");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string ArtCacheDirectory => Path.Combine(Root, "art");
}

public sealed class JsonSettingsStore(AppDataPaths paths) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Load()
    {
        if (!File.Exists(paths.SettingsFile))
            return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsFile), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A corrupt file must not block startup; the first-run wizard will ask again.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(paths.Root);
        // Write to a temp file and swap, so a crash never leaves a half-written settings file.
        var temp = paths.SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, paths.SettingsFile, overwrite: true);
    }
}
