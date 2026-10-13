using System.Runtime.CompilerServices;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Assets.Readers;
using Serilog;

namespace FuserSongManager.Infrastructure.Pak;

/// <summary>
/// Reads tagged properties sequentially instead of jumping to <c>start + declared size</c>.
/// The community tooling that builds custom Fuser songs keeps the sizes of its template
/// (Artist 25, Title 22, PickupBeats 44, IsStreamOptimized 1…) whatever the real data
/// length is, so the standard CUE4Parse reader lands on the wrong offset and loses every
/// property after the first mismatch. The v1 parser worked because it read sequentially too.
/// </summary>
public class TolerantTaggedObject : UObject
{
    public override void Deserialize(FAssetArchive Ar, long validPos)
    {
        while (Ar.Position < validPos)
        {
            var tag = new FPropertyTag(Ar, readData: false);
            if (tag.Name.IsNone)
                break;

            var dataStart = Ar.Position;
            try
            {
                tag.Tag = ReadPropertyTagType(null, Ar, tag.PropertyType.Text, tag.TagData, ReadType.NORMAL, tag.Size);
            }
            catch (Exception ex)
            {
                // The sequential read failed: trust the declared size to skip this property.
                Log.Warning(ex, "{Object}: could not read {Name} ({Type}), skipping {Size} declared bytes", Name, tag.Name.Text, tag.PropertyType.Text, tag.Size);
                Ar.Position = dataStart + tag.Size;
                continue;
            }

            var consumed = Ar.Position - dataStart;
            if (consumed != tag.Size)
                Log.Verbose("{Object}: {Name} ({Type}) declared {Size} bytes, read {Consumed}", Name, tag.Name.Text, tag.PropertyType.Text, tag.Size, consumed);

            Properties.Add(tag);
        }
    }

    // CUE4Parse keeps this reader internal; the public FPropertyTag constructor wraps it
    // with a seek to start + declared size, which is exactly what breaks on custom paks.
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ReadPropertyTagType")]
    private static extern FPropertyTagType? ReadPropertyTagType(
        FPropertyTagType? _, FAssetArchive Ar, string propertyType, FPropertyTagData? tagData, ReadType type, int size);
}

public sealed class CelSongSet : TolerantTaggedObject;

public sealed class SongCelData : TolerantTaggedObject;
