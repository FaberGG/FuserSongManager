namespace FuserSongManager.Infrastructure.Pak;

/// <summary>
/// Decodes the block-compressed textures used for album art (DXT1/BC1 from FuserCustomSongCreator, DXT5/BC3 just
/// in case) to RGBA8. Each 4x4 pixel block holds two RGB565 endpoint colours and 2-bit indices into a 4-colour
/// palette derived from them; DXT5 adds an 8-byte interpolated alpha block in front.
/// </summary>
public static class DxtDecoder
{
    public static byte[] DecodeDxt1(ReadOnlySpan<byte> data, int width, int height) => Decode(data, width, height, hasAlphaBlock: false);

    public static byte[] DecodeDxt5(ReadOnlySpan<byte> data, int width, int height) => Decode(data, width, height, hasAlphaBlock: true);

    private static byte[] Decode(ReadOnlySpan<byte> data, int width, int height, bool hasAlphaBlock)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var blockSize = hasAlphaBlock ? 16 : 8;
        if (data.Length < blocksX * blocksY * blockSize)
            throw new InvalidDataException($"The texture data is too short for {width}x{height} ({data.Length} bytes).");

        var rgba = new byte[width * height * 4];
        Span<byte> palette = stackalloc byte[16];
        Span<byte> alphas = stackalloc byte[8];

        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            var block = data.Slice((by * blocksX + bx) * blockSize, blockSize);
            ulong alphaBits = 0;
            if (hasAlphaBlock)
            {
                AlphaPalette(block[0], block[1], alphas);
                for (var i = 0; i < 6; i++)
                    alphaBits |= (ulong)block[2 + i] << (8 * i);
                block = block[8..];
            }

            var c0 = (ushort)(block[0] | block[1] << 8);
            var c1 = (ushort)(block[2] | block[3] << 8);
            ColorPalette(c0, c1, opaqueOnly: hasAlphaBlock, palette);
            var indices = (uint)(block[4] | block[5] << 8 | block[6] << 16 | block[7] << 24);

            for (var py = 0; py < 4; py++)
            for (var px = 0; px < 4; px++)
            {
                int x = bx * 4 + px, y = by * 4 + py;
                if (x >= width || y >= height)
                    continue;
                var pixel = py * 4 + px;
                var colour = (int)(indices >> (2 * pixel)) & 3;
                var target = (y * width + x) * 4;
                palette.Slice(colour * 4, 4).CopyTo(rgba.AsSpan(target, 4));
                if (hasAlphaBlock)
                    rgba[target + 3] = alphas[(int)(alphaBits >> (3 * pixel)) & 7];
            }
        }
        return rgba;
    }

    private static void ColorPalette(ushort c0, ushort c1, bool opaqueOnly, Span<byte> palette)
    {
        Rgb565(c0, palette[..4]);
        Rgb565(c1, palette.Slice(4, 4));
        for (var i = 0; i < 3; i++)
        {
            int a = palette[i], b = palette[4 + i];
            if (c0 > c1 || opaqueOnly)
            {
                palette[8 + i] = (byte)((2 * a + b) / 3);
                palette[12 + i] = (byte)((a + 2 * b) / 3);
            }
            else
            {
                palette[8 + i] = (byte)((a + b) / 2);
                palette[12 + i] = 0;
            }
        }
        palette[11] = 255;
        // DXT1 with c0 <= c1 uses the fourth colour as transparent black.
        palette[15] = (byte)(c0 > c1 || opaqueOnly ? 255 : 0);
    }

    private static void Rgb565(ushort c, Span<byte> rgba)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        rgba[0] = (byte)((r << 3) | (r >> 2));
        rgba[1] = (byte)((g << 2) | (g >> 4));
        rgba[2] = (byte)((b << 3) | (b >> 2));
        rgba[3] = 255;
    }

    private static void AlphaPalette(byte a0, byte a1, Span<byte> alphas)
    {
        alphas[0] = a0;
        alphas[1] = a1;
        if (a0 > a1)
        {
            for (var i = 1; i < 7; i++)
                alphas[1 + i] = (byte)(((7 - i) * a0 + i * a1) / 7);
        }
        else
        {
            for (var i = 1; i < 5; i++)
                alphas[1 + i] = (byte)(((5 - i) * a0 + i * a1) / 5);
            alphas[6] = 0;
            alphas[7] = 255;
        }
    }
}
