using System.Buffers.Binary;
using System.IO.Compression;

namespace RefBoardXIV.Tests;

/// <summary>
/// Les PNG écrits sont relus par un petit lecteur fait ici (décompression + filtres inverses), pour vérifier pixel par
/// pixel que l'image n'a pas changé.
/// </summary>
public class PngTests
{
    [Fact]
    public void EncodedPixelsReadBackIdentical()
    {
        var (width, height) = (37, 23);
        var rgba = new byte[width * height * 4];
        var random = new Random(42);
        for (var i = 0; i < rgba.Length; i++)
            rgba[i] = (byte)((i % 4 == 3) ? random.Next(256) : ((i / 4 % width) * 7) + random.Next(4));

        var decoded = Decode(Png.Encode(width, height, rgba, true));

        Assert.Equal((width, height, 6), (decoded.Width, decoded.Height, decoded.ColorType));
        Assert.Equal(rgba, decoded.Rgba);
    }

    [Fact]
    public void OpaqueImageIsWrittenWithoutAlpha()
    {
        var rgba = Enumerable.Range(0, 4 * 4 * 4).Select(i => i % 4 == 3 ? (byte)255 : (byte)(i * 3)).ToArray();
        var decoded = Decode(Png.Encode(4, 4, rgba, false));
        Assert.Equal(2, decoded.ColorType);
        Assert.Equal(rgba, decoded.Rgba);
    }

    [Fact]
    public void BottomUpTrueColorDib()
    {
        // 2 x 2 en 24 bits, lignes de bas en haut, complétées à 4 octets (6 + 2 octets de bourrage).
        var dib = Header(40, 2, 2, 24, 0);
        dib.AddRange([0, 0, 255, 0, 255, 0, 0, 0]); // ligne du bas : rouge, vert
        dib.AddRange([255, 0, 0, 255, 255, 255, 0, 0]); // ligne du haut : bleu, blanc

        var decoded = Decode(Png.FromDib(dib.ToArray())!);

        Assert.Equal(2, decoded.ColorType);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 255, 255, 255, 255, 0, 0, 255, 0, 255, 0, 255 }, decoded.Rgba);
    }

    [Fact]
    public void ZeroAlphaMeansOpaque()
    {
        // 32 bits sans masques, octet de transparence laissé à 0 : l'image est opaque, pas invisible.
        var dib = Header(40, 1, -1, 32, 0);
        dib.AddRange([10, 20, 30, 0]);
        var decoded = Decode(Png.FromDib(dib.ToArray())!);
        Assert.Equal(2, decoded.ColorType);
        Assert.Equal(new byte[] { 30, 20, 10, 255 }, decoded.Rgba);
    }

    [Fact]
    public void AlphaMaskOfVersion5HeaderIsKept()
    {
        // En-tête V5 (124 octets) avec masques : rouge, vert, bleu, alpha. Image de haut en bas (hauteur négative).
        var dib = Header(124, 2, -1, 32, 3);
        var masks = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(masks, 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(4), 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(8), 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(12), 0xFF000000);
        for (var i = 0; i < 16; i++)
            dib[40 + i] = masks[i];
        dib.AddRange([1, 2, 3, 128, 4, 5, 6, 255]);

        var decoded = Decode(Png.FromDib(dib.ToArray())!);

        Assert.Equal(6, decoded.ColorType);
        Assert.Equal(new byte[] { 3, 2, 1, 128, 6, 5, 4, 255 }, decoded.Rgba);
    }

    [Fact]
    public void UnsupportedDibGivesNothing()
    {
        Assert.Null(Png.FromDib(Header(40, 2, 2, 8, 0).Concat(new byte[1024 + 8]).ToArray())); // 256 couleurs
        Assert.Null(Png.FromDib(Header(40, 2, 2, 24, 0).ToArray())); // pixels manquants
        Assert.Null(Png.FromDib(new byte[10]));
    }

    [Fact]
    public void CrcMatchesReference()
    {
        // CRC connu du bloc IEND vide de tous les PNG.
        Assert.Equal(0xAE426082u, Png.Crc("IEND"u8, []));
    }

    private static List<byte> Header(int size, int width, int height, ushort bitCount, uint compression)
    {
        var header = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(header, size);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), compression);
        return [.. header];
    }

    private sealed record Decoded(int Width, int Height, int ColorType, byte[] Rgba);

    private static Decoded Decode(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        int width = 0, height = 0, colorType = 0;
        var compressed = new MemoryStream();
        var position = 8;
        while (position < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            var type = png.AsSpan(position + 4, 4);
            var data = png.AsSpan(position + 8, length);
            Assert.Equal(Png.Crc(type, data), BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position + 8 + length)));
            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                colorType = data[9];
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }

            position += 12 + length;
        }

        compressed.Position = 0;
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
            zlib.CopyTo(raw);

        var channels = colorType == 6 ? 4 : 3;
        var rowLength = width * channels;
        var bytes = raw.ToArray();
        Assert.Equal(height * (rowLength + 1), bytes.Length);

        var rgba = new byte[width * height * 4];
        var previous = new byte[rowLength];
        for (var y = 0; y < height; y++)
        {
            var filter = bytes[y * (rowLength + 1)];
            var row = bytes.AsSpan((y * (rowLength + 1)) + 1, rowLength).ToArray();
            for (var i = 0; i < rowLength; i++)
            {
                var left = i >= channels ? row[i - channels] : 0;
                var upLeft = i >= channels ? previous[i - channels] : 0;
                row[i] += (byte)(filter switch
                {
                    1 => left,
                    2 => previous[i],
                    3 => (left + previous[i]) / 2,
                    4 => Png.Paeth(left, previous[i], upLeft),
                    _ => 0,
                });
            }

            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < 4; c++)
                    rgba[(((y * width) + x) * 4) + c] = c < channels ? row[(x * channels) + c] : (byte)255;
            }

            previous = row;
        }

        return new Decoded(width, height, colorType, rgba);
    }
}
