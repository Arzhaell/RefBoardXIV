using System;
using System.Buffers.Binary;
using System.IO;

namespace RefBoardXIV;

/// <summary>
/// Taille d'une image lue dans l'en-tête du fichier, sans la décoder : de quoi placer les images côte à côte dès qu'on
/// les ajoute. Formats lus : PNG, JPEG, GIF, BMP, WebP ; pour les autres, la taille vient de l'image une fois chargée.
/// </summary>
internal static class ImageSize
{
    public static (int Width, int Height)? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            // Les marqueurs de taille d'un JPEG peuvent venir après les métadonnées (EXIF, miniature) : on lit large.
            var header = new byte[(int)Math.Min(stream.Length, 256 * 1024)];
            stream.ReadExactly(header);
            return Read(header);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static (int Width, int Height)? Read(ReadOnlySpan<byte> data)
    {
        var size = ReadPng(data) ?? ReadGif(data) ?? ReadBmp(data) ?? ReadWebP(data) ?? ReadJpeg(data);
        return size is var (width, height) && width > 0 && height > 0 ? (width, height) : null;
    }

    private static (int, int)? ReadPng(ReadOnlySpan<byte> data) =>
        data.Length >= 24 && data[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A])
            ? (BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]))
            : null;

    private static (int, int)? ReadGif(ReadOnlySpan<byte> data) =>
        data.Length >= 10 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F'
            ? (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]))
            : null;

    // Une hauteur négative veut dire que l'image est enregistrée de haut en bas.
    private static (int, int)? ReadBmp(ReadOnlySpan<byte> data) =>
        data.Length >= 26 && data[0] == 'B' && data[1] == 'M'
            ? (BinaryPrimitives.ReadInt32LittleEndian(data[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..])))
            : null;

    private static (int, int)? ReadWebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WEBP"u8))
            return null;

        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
            return (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
        if (chunk.SequenceEqual("VP8L"u8))
            return (1 + (data[21] | ((data[22] & 0x3F) << 8)), 1 + ((data[22] >> 6) | (data[23] << 2) | ((data[24] & 0x0F) << 10)));
        if (chunk.SequenceEqual("VP8X"u8))
            return (1 + (data[24] | (data[25] << 8) | (data[26] << 16)), 1 + (data[27] | (data[28] << 8) | (data[29] << 16)));
        return null;
    }

    // Parcourt les blocs du JPEG jusqu'à celui qui décrit l'image (SOF0 à SOF15, sauf DHT, JPG et DAC qui ont d'autres rôles).
    private static (int, int)? ReadJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return null;

        var position = 2;
        while (position + 9 < data.Length)
        {
            if (data[position] != 0xFF)
                return null;
            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(position + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(position + 5)..]));

            position += 2 + BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
        }

        return null;
    }
}
