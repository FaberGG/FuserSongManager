using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FuserSongManager.Infrastructure.Pak;

/// <summary>
/// Turns a Harmonix MOGG into plain Ogg Vorbis. A MOGG is a small header (version, Ogg offset, seek map)
/// followed by the Ogg stream, which version 0x0B encrypts with AES-128 in counter mode.
/// Custom songs made with FuserCustomSongCreator always use 0x0B (its moggcrypt writes nothing else).
/// </summary>
public static class MoggCrypto
{
    public const int Unencrypted = 0x0A;
    public const int AesCtr = 0x0B;

    // The public 0x0B key, as used by the moggcrypt tools (FuserCustomSongCreator/moggcrypt/keys.h).
    private static readonly byte[] Key0B = [0x37, 0xB2, 0xE2, 0xB9, 0x1C, 0x74, 0xFA, 0x9E, 0x38, 0x81, 0x08, 0xEA, 0x36, 0x23, 0xDB, 0xE4];

    private const int BlockSize = 16;
    private const int BlocksPerBatch = 4096;

    public static int Version(ReadOnlySpan<byte> mogg) =>
        mogg.Length >= 8 ? BinaryPrimitives.ReadInt32LittleEndian(mogg) : throw new InvalidDataException("The audio data is too short to be a MOGG file.");

    public static byte[] ToOgg(ReadOnlySpan<byte> mogg)
    {
        var version = Version(mogg);
        var oggOffset = BinaryPrimitives.ReadInt32LittleEndian(mogg[4..]);
        if (oggOffset < 8 || oggOffset > mogg.Length)
            throw new InvalidDataException("The MOGG header is damaged (bad Ogg offset).");

        return version switch
        {
            Unencrypted => mogg[oggOffset..].ToArray(),
            // 0x0B stores the 16-byte initial counter right before the Ogg data.
            AesCtr when oggOffset >= 8 + BlockSize => Transform(mogg[oggOffset..], mogg.Slice(oggOffset - BlockSize, BlockSize)),
            _ => throw new NotSupportedException(
                $"This song's audio uses MOGG encryption version 0x{version:X2}, which cannot be played (only 0x0A and 0x0B are supported)."),
        };
    }

    /// <summary>
    /// AES-CTR as moggcrypt implements it: keystream block = AES-ECB(key, counter), and the low 64 bits of
    /// the counter (little-endian) go up by one per 16-byte block. Encrypting and decrypting are the same operation.
    /// </summary>
    public static byte[] Transform(ReadOnlySpan<byte> data, ReadOnlySpan<byte> initialCounter)
    {
        using var aes = Aes.Create();
        aes.Key = Key0B;

        var output = new byte[data.Length];
        var counterLow = BinaryPrimitives.ReadUInt64LittleEndian(initialCounter);
        var counterHigh = initialCounter[8..].ToArray();
        var counters = new byte[BlocksPerBatch * BlockSize];
        var keystream = new byte[counters.Length];

        for (var offset = 0; offset < data.Length; offset += counters.Length)
        {
            // Encrypt a whole batch of counters in one ECB call: much faster than one call per block.
            var blocks = Math.Min(BlocksPerBatch, (data.Length - offset + BlockSize - 1) / BlockSize);
            for (var b = 0; b < blocks; b++)
            {
                var counter = counters.AsSpan(b * BlockSize, BlockSize);
                BinaryPrimitives.WriteUInt64LittleEndian(counter, counterLow++);
                counterHigh.CopyTo(counter[8..]);
            }
            var length = blocks * BlockSize;
            aes.EncryptEcb(counters.AsSpan(0, length), keystream.AsSpan(0, length), PaddingMode.None);

            var count = Math.Min(length, data.Length - offset);
            for (var i = 0; i < count; i++)
                output[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
        }
        return output;
    }
}
