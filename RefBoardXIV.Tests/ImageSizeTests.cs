using System.Buffers.Binary;

namespace RefBoardXIV.Tests;

/// <summary>En-têtes d'images fabriqués à la main (seuls les octets lus par <see cref="ImageSize"/> comptent).</summary>
public class ImageSizeTests
{
    [Fact]
    public void ReadsPng()
    {
        var data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), 1920);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 1080);
        Assert.Equal((1920, 1080), ImageSize.Read(data));
    }

    [Fact]
    public void ReadsGif()
    {
        var data = new byte[13];
        "GIF89a"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 320);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 240);
        Assert.Equal((320, 240), ImageSize.Read(data));
    }

    [Fact]
    public void ReadsTopDownBmp()
    {
        var data = new byte[54];
        "BM"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), 640);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), -480);
        Assert.Equal((640, 480), ImageSize.Read(data));
    }

    [Fact]
    public void ReadsJpegAfterMetadata()
    {
        // SOI, un bloc APP1 (EXIF) de 20 octets, puis SOF0 : précision, hauteur 600, largeur 800.
        var data = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x14 };
        data.AddRange(new byte[18]);
        data.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, 0x02, 0x58, 0x03, 0x20, 0x03]);
        data.AddRange(new byte[12]);
        Assert.Equal((800, 600), ImageSize.Read(data.ToArray()));
    }

    [Fact]
    public void ReadsWebPVariants()
    {
        // VP8X : largeur et hauteur moins un, sur 3 octets.
        var extended = WebP("VP8X");
        extended[24] = 0xFF;
        extended[25] = 0x03; // 1023 + 1 = 1024
        extended[27] = 0xFF;
        extended[28] = 0x02; // 767 + 1 = 768
        Assert.Equal((1024, 768), ImageSize.Read(extended));

        var lossy = WebP("VP8 ");
        BinaryPrimitives.WriteUInt16LittleEndian(lossy.AsSpan(26), 500);
        BinaryPrimitives.WriteUInt16LittleEndian(lossy.AsSpan(28), 300);
        Assert.Equal((500, 300), ImageSize.Read(lossy));

        // VP8L : 14 bits pour la largeur moins un, puis 14 pour la hauteur moins un.
        var lossless = WebP("VP8L");
        var bits = (uint)(400 - 1) | ((uint)(200 - 1) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(lossless.AsSpan(21), bits);
        Assert.Equal((400, 200), ImageSize.Read(lossless));
    }

    [Fact]
    public void UnknownOrBrokenDataGivesNothing()
    {
        Assert.Null(ImageSize.Read("hello world, not an image"u8.ToArray()));
        Assert.Null(ImageSize.Read(new byte[] { 0xFF, 0xD8, 0xFF }));
        Assert.Null(ImageSize.Read([]));
    }

    private static byte[] WebP(string chunk)
    {
        var data = new byte[40];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(data, 12);
        return data;
    }
}
