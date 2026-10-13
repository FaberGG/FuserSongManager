using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace FuserSongManager.Infrastructure.Pak;

/// <summary>One resource of a Harmonix audio package (<c>HmxAudio::PackageFile</c> in FuserCustomSongCreator).</summary>
/// <param name="FileName">E.g. <c>C:/Song_ld_0.mogg</c> as stored by the creator.</param>
/// <param name="Data">The MOGG for a <c>MoggSampleResource</c>; the patch text for a <c>FusionPatchResource</c>.</param>
public sealed record HmxPackageFile(string FileName, string FileType, byte[] Data);

/// <summary>A keyzone of a fusion patch: which sample plays for which key.</summary>
/// <param name="Label">Free text chosen by the author, e.g. <c>Major</c>, <c>Minor</c>, <c>Rap</c>; may be empty.</param>
/// <param name="MinNote">Lowest MIDI note the zone answers to; with <paramref name="MaxNote"/> this decides the key it is used for.</param>
public sealed record FusionKeyzone(string Label, string SamplePath, bool Unpitched, int MinNote = 0, int MaxNote = 127);

/// <summary>
/// Reads the audio package inside a <c>*_fusion.uexp</c>. Each resource is serialized as
/// <c>i32 unk, FString fileName, u32 0, FString fileType, u64 size</c> followed by its data; a MOGG resource
/// has a 32-byte <c>mogs</c> header before the MOGG. The export's UE header before the package is skipped by
/// locating each resource through its type name, which keeps this independent of the property layout.
/// </summary>
public static class HmxAudioPackage
{
    public const string MoggSampleResource = "MoggSampleResource";
    public const string FusionPatchResource = "FusionPatchResource";
    private const int MoggHeaderSize = 32;

    public static IReadOnlyList<HmxPackageFile> Parse(ReadOnlySpan<byte> uexp)
    {
        var files = new List<(int Position, HmxPackageFile File)>();
        foreach (var type in new[] { MoggSampleResource, FusionPatchResource })
        {
            // FString: i32 length (with the terminator), ASCII bytes, '\0'.
            var marker = new byte[4 + type.Length + 1];
            BinaryPrimitives.WriteInt32LittleEndian(marker, type.Length + 1);
            Encoding.ASCII.GetBytes(type).CopyTo(marker, 4);

            var searchFrom = 0;
            while (searchFrom < uexp.Length)
            {
                var found = uexp[searchFrom..].IndexOf(marker);
                if (found < 0)
                    break;
                var markerAt = searchFrom + found;
                var dataAt = markerAt + marker.Length + 8;
                if (dataAt > uexp.Length)
                    throw new InvalidDataException($"The audio package is truncated after a {type}.");
                var size = BinaryPrimitives.ReadInt64LittleEndian(uexp[(markerAt + marker.Length)..]);

                byte[] data;
                if (type == MoggSampleResource)
                {
                    if (dataAt + MoggHeaderSize > uexp.Length)
                        throw new InvalidDataException("The audio package is truncated inside a MOGG header.");
                    var moggSize = BinaryPrimitives.ReadInt32LittleEndian(uexp[(dataAt + 28)..]);
                    var moggAt = dataAt + MoggHeaderSize;
                    if (moggSize < 0 || moggAt + moggSize > uexp.Length)
                        throw new InvalidDataException("The audio package is truncated inside a MOGG.");
                    data = uexp.Slice(moggAt, moggSize).ToArray();
                    searchFrom = moggAt + moggSize;
                }
                else
                {
                    if (size < 0 || dataAt + size > uexp.Length)
                        throw new InvalidDataException("The audio package is truncated inside a fusion patch.");
                    data = uexp.Slice(dataAt, (int)size).ToArray();
                    searchFrom = dataAt + (int)size;
                }

                files.Add((markerAt, new HmxPackageFile(ReadFileNameBefore(uexp, markerAt) ?? "", type, data)));
            }
        }
        return files.OrderBy(f => f.Position).Select(f => f.File).ToList();
    }

    /// <summary>The fileName FString that ends 4 bytes (the <c>u32 0</c>) before the type's FString.</summary>
    private static string? ReadFileNameBefore(ReadOnlySpan<byte> uexp, int typeAt)
    {
        var end = typeAt - 4;   // exclusive; the byte before is the name's '\0'
        if (end < 5 || uexp[end - 1] != 0)
            return null;
        for (var length = 1; length <= 1024 && end - length - 4 >= 0; length++)
        {
            var lengthAt = end - length - 4;
            if (BinaryPrimitives.ReadInt32LittleEndian(uexp[lengthAt..]) == length)
                return Encoding.ASCII.GetString(uexp.Slice(lengthAt + 4, length - 1));
        }
        return null;
    }

    private static readonly Regex ZoneLabel = new(@"\(zone_label\s*""(?<v>[^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex SamplePath = new(@"\(sample_path\s*""(?<v>[^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex UnpitchedFlag = new(@"\(unpitched\s+(?<v>\d)\)", RegexOptions.CultureInvariant);
    private static readonly Regex MinNote = new(@"\(min_note\s+(?<v>\d+)\)", RegexOptions.CultureInvariant);
    private static readonly Regex MaxNote = new(@"\(max_note\s+(?<v>\d+)\)", RegexOptions.CultureInvariant);

    /// <summary>The keyzones of a fusion patch, in file order.</summary>
    public static IReadOnlyList<FusionKeyzone> ParseKeyzones(string patchText) =>
        KeyzoneBodies(patchText)
            .Select(body => new FusionKeyzone(
                ZoneLabel.Match(body) is { Success: true } label ? label.Groups["v"].Value : "",
                SamplePath.Match(body) is { Success: true } path ? path.Groups["v"].Value : "",
                UnpitchedFlag.Match(body) is { Success: true } flag && flag.Groups["v"].Value == "1",
                MinNote.Match(body) is { Success: true } min ? int.Parse(min.Groups["v"].Value) : 0,
                MaxNote.Match(body) is { Success: true } max ? int.Parse(max.Groups["v"].Value) : 127))
            .ToList();

    /// <summary>The text of each <c>(keyzone …)</c> list, found by matching parentheses outside quoted strings.</summary>
    private static IEnumerable<string> KeyzoneBodies(string text)
    {
        const string open = "(keyzone";
        for (var start = text.IndexOf(open, StringComparison.Ordinal); start >= 0; start = text.IndexOf(open, start + 1, StringComparison.Ordinal))
        {
            int depth = 0, i = start;
            var quoted = false;
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"') quoted = !quoted;
                else if (quoted) continue;
                else if (c == '(') depth++;
                else if (c == ')' && --depth == 0) break;
            }
            if (i >= text.Length)
                yield break;   // unbalanced: ignore the rest
            yield return text[(start + open.Length)..i];
            start = i;
        }
    }
}
