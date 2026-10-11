using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Numerics;

namespace RefBoardXIV;

/// <summary>
/// Écriture d'images PNG. Une image collée arrive souvent en BMP (format DIB du presse-papiers), sans compression :
/// on la convertit pour qu'elle prenne beaucoup moins de place dans le tableau.
/// </summary>
internal static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// PNG d'une image du presse-papiers au format DIB, ou null si ce format n'est pas géré (seuls les 24 et 32 bits
    /// par pixel, de loin les plus courants, sont convertis).
    /// </summary>
    public static byte[]? FromDib(byte[] dib)
    {
        if (dib.Length < 40)
            return null;
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (headerSize < 40 || headerSize > dib.Length)
            return null;

        var width = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4));
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8));
        var height = Math.Abs(rawHeight);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));

        // 0 = BI_RGB (sans masques), 3 = BI_BITFIELDS, 6 = BI_ALPHABITFIELDS (masques de couleur).
        if (width <= 0 || height <= 0 || width > 32768 || height > 32768 || colorsUsed > 256)
            return null;
        if (!(bitCount == 24 && compression == 0) && !(bitCount == 32 && compression is 0 or 3 or 6))
            return null;

        uint red = 0x00FF0000, green = 0x0000FF00, blue = 0x000000FF, alpha = 0xFF000000;
        var masksAfterHeader = 0;
        if (compression != 0)
        {
            // Masques dans l'en-tête (V4, V5), ou juste après un en-tête court.
            if (headerSize == 40)
                masksAfterHeader = compression == 6 ? 16 : 12;
            if (40 + Math.Max(masksAfterHeader, 12) > dib.Length)
                return null;
            red = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(40));
            green = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(44));
            blue = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(48));
            alpha = headerSize >= 56 || compression == 6 ? BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(52)) : 0;
        }

        var stride = ((width * bitCount) + 31) / 32 * 4;
        var pixelOffset = headerSize + masksAfterHeader + ((int)colorsUsed * 4);
        if (pixelOffset + ((long)stride * height) > dib.Length)
            return null;

        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            // Lignes rangées de bas en haut, sauf si la hauteur est négative.
            var source = dib.AsSpan(pixelOffset + (stride * (rawHeight < 0 ? y : height - 1 - y)), stride);
            var target = rgba.AsSpan(y * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                if (bitCount == 24)
                {
                    target[(x * 4) + 0] = source[(x * 3) + 2];
                    target[(x * 4) + 1] = source[(x * 3) + 1];
                    target[(x * 4) + 2] = source[x * 3];
                    target[(x * 4) + 3] = 255;
                    continue;
                }

                var pixel = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
                target[(x * 4) + 0] = Channel(pixel, red);
                target[(x * 4) + 1] = Channel(pixel, green);
                target[(x * 4) + 2] = Channel(pixel, blue);
                target[(x * 4) + 3] = alpha == 0 ? (byte)255 : Channel(pixel, alpha);
            }
        }

        // En 32 bits sans masques, beaucoup de logiciels laissent l'octet de transparence à 0 : l'image est alors opaque.
        var hasAlpha = false;
        var anyVisible = false;
        for (var i = 3; i < rgba.Length; i += 4)
        {
            hasAlpha |= rgba[i] != 255;
            anyVisible |= rgba[i] != 0;
        }

        if (!anyVisible)
            hasAlpha = false;

        return Encode(width, height, rgba, hasAlpha);
    }

    /// <summary>
    /// PNG d'une image en pixels RGBA (4 octets par pixel, lignes de haut en bas). Sans transparence, la couche alpha
    /// n'est pas écrite.
    /// </summary>
    public static byte[] Encode(int width, int height, byte[] rgba, bool hasAlpha)
    {
        var channels = hasAlpha ? 4 : 3;
        var rowLength = width * channels;

        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // 8 bits par couleur
        header[9] = (byte)(hasAlpha ? 6 : 2); // RGBA ou RGB
        WriteChunk(output, "IHDR"u8, header);

        using (var data = new MemoryStream())
        {
            using (var zlib = new ZLibStream(data, CompressionLevel.Optimal, true))
            {
                var previous = new byte[rowLength];
                var current = new byte[rowLength];
                var filtered = new byte[rowLength + 1];
                var best = new byte[rowLength + 1];
                for (var y = 0; y < height; y++)
                {
                    var source = rgba.AsSpan(y * width * 4, width * 4);
                    for (var x = 0; x < width; x++)
                        source.Slice(x * 4, channels).CopyTo(current.AsSpan(x * channels));

                    FilterRow(current, previous, channels, filtered, best);
                    zlib.Write(best);
                    (previous, current) = (current, previous);
                }
            }

            WriteChunk(output, "IDAT"u8, data.ToArray());
        }

        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    // Valeur d'une couleur selon son masque, ramenée sur 8 bits.
    private static byte Channel(uint pixel, uint mask)
    {
        if (mask == 0)
            return 0;
        var bits = BitOperations.PopCount(mask);
        var value = (pixel & mask) >> BitOperations.TrailingZeroCount(mask);
        return bits == 8 ? (byte)value : (byte)(value * 255 / ((1u << bits) - 1));
    }

    // Chaque ligne est écrite avec le filtre qui la rend la plus facile à compresser (méthode habituelle : la plus
    // petite somme des écarts). Filtres du format PNG : 0 aucun, 1 gauche, 2 haut, 3 moyenne, 4 Paeth.
    private static void FilterRow(byte[] row, byte[] above, int channels, byte[] candidate, byte[] best)
    {
        var bestScore = long.MaxValue;
        for (byte filter = 0; filter <= 4; filter++)
        {
            candidate[0] = filter;
            long score = 0;
            for (var i = 0; i < row.Length; i++)
            {
                var left = i >= channels ? row[i - channels] : 0;
                var up = above[i];
                var upLeft = i >= channels ? above[i - channels] : 0;
                var predicted = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };
                var value = (byte)(row[i] - predicted);
                candidate[i + 1] = value;
                score += value < 128 ? value : 256 - value; // écart à zéro, l'octet lu comme un nombre signé
            }

            if (score < bestScore)
            {
                bestScore = score;
                candidate.CopyTo(best, 0);
            }
        }
    }

    internal static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        if (toLeft <= toUp && toLeft <= toUpLeft)
            return left;
        return toUp <= toUpLeft ? up : upLeft;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(type, data));
        output.Write(number);
    }

    internal static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
