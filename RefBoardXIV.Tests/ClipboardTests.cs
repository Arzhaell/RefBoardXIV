using System.Buffers.Binary;

namespace RefBoardXIV.Tests;

public class ClipboardTests
{
    private static byte[] Dib(int headerSize, ushort bitCount, uint compression, uint colorsUsed, int pixelBytes)
    {
        var dib = new byte[headerSize + pixelBytes];
        BinaryPrimitives.WriteInt32LittleEndian(dib, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(32), colorsUsed);
        return dib;
    }

    private static int PixelOffset(byte[] bmp) => BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10));

    [Fact]
    public void TrueColorPixelsFollowHeader()
    {
        var bmp = Clipboard.BmpFromDib(Dib(40, 32, 0, 0, 16));
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(bmp.Length, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2)));
        Assert.Equal(14 + 40, PixelOffset(bmp));
    }

    [Fact]
    public void ColorMasksAreSkipped()
    {
        Assert.Equal(14 + 40 + 12, PixelOffset(Clipboard.BmpFromDib(Dib(40, 32, 3, 0, 16))));
        // En-tête V5 : les masques font partie de l'en-tête.
        Assert.Equal(14 + 124, PixelOffset(Clipboard.BmpFromDib(Dib(124, 32, 3, 0, 16))));
    }

    [Fact]
    public void PaletteIsSkipped()
    {
        Assert.Equal(14 + 40 + (256 * 4), PixelOffset(Clipboard.BmpFromDib(Dib(40, 8, 0, 0, 4))));
        Assert.Equal(14 + 40 + (16 * 4), PixelOffset(Clipboard.BmpFromDib(Dib(40, 8, 0, 16, 4))));
    }
}
