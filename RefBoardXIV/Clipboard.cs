using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RefBoardXIV;

/// <summary>
/// Ce qu'on peut coller dans un tableau : des fichiers copiés dans l'explorateur, ou une image copiée, en PNG ou au
/// format DIB de Windows (à convertir avec <see cref="Clipboard.ImageFromDib"/>).
/// </summary>
internal sealed record ClipboardContent(IReadOnlyList<string> Files, byte[]? Png, byte[]? Dib);

/// <summary>Lecture du presse-papiers de Windows (lecture seule : on n'y écrit jamais).</summary>
internal static class Clipboard
{
    private const uint CfDib = 8;
    private const uint CfHdrop = 15;
    private const uint CfDibV5 = 17;

    /// <summary>Contenu collable, ou null s'il n'y a rien d'utile (ou si le presse-papiers est occupé par un autre programme).</summary>
    public static ClipboardContent? Read()
    {
        if (!OpenClipboard(0))
            return null;
        try
        {
            if (IsClipboardFormatAvailable(CfHdrop))
                return new ClipboardContent(ReadFiles(GetClipboardData(CfHdrop)), null, null);

            // Beaucoup de logiciels donnent aussi l'image en PNG (avec sa transparence) : déjà compressée, on la prend.
            var png = RegisterClipboardFormatW("PNG");
            if (png != 0 && IsClipboardFormatAvailable(png) && ReadBytes(GetClipboardData(png)) is { } pngData)
                return new ClipboardContent([], pngData, null);

            // Sinon le format d'image de Windows ; en version 5 d'abord, qui garde le mieux la transparence.
            foreach (var format in (uint[])[CfDibV5, CfDib])
            {
                if (IsClipboardFormatAvailable(format) && ReadBytes(GetClipboardData(format)) is { } dib)
                    return new ClipboardContent([], null, dib);
            }

            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Fichier image d'une image du presse-papiers au format DIB : en PNG, ou en BMP pour les rares formats que
    /// <see cref="Png.FromDib"/> ne convertit pas. Peut prendre un moment sur une grande image : à faire en arrière-plan.
    /// </summary>
    public static (byte[] Data, string Extension) ImageFromDib(byte[] dib) =>
        Png.FromDib(dib) is { } png ? (png, ".png") : (BmpFromDib(dib), ".bmp");

    /// <summary>
    /// Fichier .bmp à partir d'une image du presse-papiers (format DIB) : il ne manque que l'en-tête du fichier, qui dit où
    /// commencent les pixels.
    /// </summary>
    public static byte[] BmpFromDib(byte[] dib)
    {
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));

        // Masques de couleur rangés après un en-tête court (BI_BITFIELDS = 3, BI_ALPHABITFIELDS = 6).
        var masks = headerSize == 40 ? compression switch { 3 => 12, 6 => 16, _ => 0 } : 0;
        var palette = (int)(colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1u << bitCount : 0) * 4;
        var pixelOffset = 14 + headerSize + masks + palette;

        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), pixelOffset);
        dib.CopyTo(file, 14);
        return file;
    }

    private static List<string> ReadFiles(nint drop)
    {
        var files = new List<string>();
        if (drop == 0)
            return files;

        var count = DragQueryFileW(drop, uint.MaxValue, null, 0);
        for (uint i = 0; i < count; i++)
        {
            var length = DragQueryFileW(drop, i, null, 0);
            var buffer = new char[length + 1];
            DragQueryFileW(drop, i, buffer, (uint)buffer.Length);
            files.Add(new string(buffer, 0, (int)length));
        }

        return files;
    }

    private static byte[]? ReadBytes(nint handle)
    {
        if (handle == 0)
            return null;
        var pointer = GlobalLock(handle);
        if (pointer == 0)
            return null;
        try
        {
            var data = new byte[(int)GlobalSize(handle)];
            Marshal.Copy(pointer, data, 0, data.Length);
            return data;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(nint drop, uint index, char[]? file, uint length);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(nint memory);
}
