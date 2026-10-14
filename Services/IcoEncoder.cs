using System.Buffers.Binary;

namespace TrayClock.Services;

/// <summary>Packs 0xAARRGGBB pixels into a single-image 32-bit ICO. No WinRT dependency, so it is testable.</summary>
internal static class IcoEncoder
{
    /// <summary>Offset of the DIB inside the encoded bytes (ICONDIR + one ICONDIRENTRY).</summary>
    public const int ImageOffset = 6 + 16;

    /// <summary>Bytes before the first pixel row (ICONDIR, ICONDIRENTRY, BITMAPINFOHEADER).</summary>
    public const int PixelOffset = ImageOffset + BitmapInfoHeaderSize;

    private const int BitmapInfoHeaderSize = 40;

    /// <param name="pixels">Row-major, top-down, straight alpha; <paramref name="size"/> squared long.</param>
    /// <param name="size">Icon width and height in pixels (1-255).</param>
    public static byte[] Encode(uint[] pixels, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 255);
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, size * size, nameof(pixels));

        int pixelBytes = size * size * 4;
        int maskBytes = ((size + 31) / 32) * 4 * size; // 1bpp AND mask, rows padded to 4 bytes; left clear
        int imageBytes = BitmapInfoHeaderSize + pixelBytes + maskBytes;
        byte[] ico = new byte[ImageOffset + imageBytes];
        Span<byte> span = ico;

        // ICONDIR: reserved, type 1 (icon), one image.
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 1);

        // ICONDIRENTRY: width, height, no palette, reserved, planes, bit count, size, offset.
        span[6] = (byte)size;
        span[7] = (byte)size;
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], imageBytes);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], ImageOffset);

        // BITMAPINFOHEADER: the height covers the color bitmap and the mask.
        Span<byte> header = span[ImageOffset..];
        BinaryPrimitives.WriteInt32LittleEndian(header, BitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], size);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], size * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], pixelBytes + maskBytes);

        // Rows are stored bottom-up; 0xAARRGGBB little-endian is the DIB's B, G, R, A byte order.
        Span<byte> bits = header[BitmapInfoHeaderSize..];
        for (int y = 0; y < size; y++)
        {
            Span<byte> row = bits[((size - 1 - y) * size * 4)..];
            for (int x = 0; x < size; x++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(row[(x * 4)..], pixels[(y * size) + x]);
            }
        }

        return ico;
    }

    /// <summary>Scales a color's alpha by <paramref name="coverage"/> (0-1).</summary>
    public static uint WithCoverage(uint color, double coverage)
    {
        uint alpha = (uint)Math.Round((color >> 24) * coverage);
        return (alpha << 24) | (color & 0x00FFFFFF);
    }
}
