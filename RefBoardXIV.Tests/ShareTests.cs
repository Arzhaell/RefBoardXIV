using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;

namespace RefBoardXIV.Tests;

/// <summary>Partage d'un tableau dans un fichier .refboard. Chaque test travaille dans son propre dossier temporaire.</summary>
public sealed class ShareTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "RefBoardXIV.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> warnings = [];

    public ShareTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, true);

    // Deux joueurs : chacun a ses propres tableaux.
    private BoardStore Sender() => new(Path.Combine(folder, "sender"), warnings.Add);

    private BoardStore Receiver() => new(Path.Combine(folder, "receiver"), warnings.Add);

    private string SharedFile() => Path.Combine(folder, "shared.refboard");

    private string SourcePng(string name, int width, int height)
    {
        var data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        data[32] = (byte)name.Length;
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    // Fichier .refboard fabriqué à la main : un board.json et les entrées données (nom → contenu).
    private string HandMadeFile(Board board, params (string Name, byte[] Data)[] entries)
    {
        var path = SharedFile();
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(BoardStore.BoardFile).Open()))
            writer.Write(JsonSerializer.Serialize(board));
        foreach (var (name, data) in entries)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(data);
        }

        return path;
    }

    [Fact]
    public void SharedBoardArrivesIdentical()
    {
        var sender = Sender();
        var board = sender.Create("Tenues");
        var images = sender.AddFiles(board, [SourcePng("a.png", 200, 100), SourcePng("b.png", 30, 30)], new Vector2(10, 20));
        images[0].CropTo(new Vector2(60, 45), new Vector2(160, 95));
        board.ViewX = 5;
        board.Zoom = 2f;

        Assert.True(sender.Export(board, SharedFile()));
        var receiver = Receiver();
        var received = receiver.Import(SharedFile());

        Assert.NotNull(received);
        Assert.NotEqual(board.Id, received.Id);
        Assert.Equal("Tenues", received.Name);
        Assert.Equal((5f, 2f), (received.ViewX, received.Zoom));
        Assert.Equal(2, received.Images.Count);
        for (var i = 0; i < 2; i++)
        {
            var (sent, got) = (images[i], received.Images[i]);
            Assert.Equal((sent.X, sent.Y, sent.Width, sent.Height), (got.X, got.Y, got.Width, got.Height));
            Assert.Equal((sent.CropLeft, sent.CropTop, sent.CropRight, sent.CropBottom), (got.CropLeft, got.CropTop, got.CropRight, got.CropBottom));
            Assert.Equal(File.ReadAllBytes(sender.PathOf(board, sent)), File.ReadAllBytes(receiver.PathOf(received, got)));
        }

        // Le tableau reçu est enregistré : il est encore là au prochain lancement.
        Assert.Equal("Tenues", Assert.Single(Receiver().Boards).Name);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ImportedBoardGetsAFreeName()
    {
        var sender = Sender();
        sender.Export(sender.Create("Tenues"), SharedFile());

        var receiver = Receiver();
        receiver.Create("Tenues");
        var first = receiver.Import(SharedFile());
        var second = receiver.Import(SharedFile());

        Assert.Equal(("Tenues (2)", "Tenues (3)"), (first?.Name, second?.Name));
        Assert.Equal(3, receiver.Boards.Count);
    }

    [Fact]
    public void FileNamesInsideTheFileNeverBecomePaths()
    {
        var board = new Board { Name = "Piégé" };
        board.Images.Add(new BoardImage { File = "../../evil.png", Width = 10, Height = 10 });
        board.Images.Add(new BoardImage { File = "C:/evil2.png", Width = 10, Height = 10 });
        var data = File.ReadAllBytes(SourcePng("a.png", 10, 10));
        var path = HandMadeFile(board, ("../../evil.png", data), ("C:/evil2.png", data));

        var receiver = Receiver();
        var received = receiver.Import(path);

        Assert.NotNull(received);
        Assert.Equal(2, received.Images.Count);
        Assert.All(received.Images, image =>
        {
            Assert.Equal(Path.GetFileName(image.File), image.File);
            Assert.Equal(data, File.ReadAllBytes(receiver.PathOf(received, image)));
        });
        Assert.False(File.Exists(Path.Combine(folder, "evil.png")));
        Assert.False(File.Exists(Path.Combine(folder, "receiver", "evil.png")));
    }

    [Fact]
    public void MissingOrUnknownFilesAreLeftOut()
    {
        var board = new Board { Name = "Incomplet" };
        board.Images.Add(new BoardImage { File = "here.png" });
        board.Images.Add(new BoardImage { File = "missing.png" });
        board.Images.Add(new BoardImage { File = "script.exe" });
        var data = File.ReadAllBytes(SourcePng("a.png", 10, 10));
        var path = HandMadeFile(board, ("here.png", data), ("script.exe", data));

        var received = Receiver().Import(path);

        Assert.NotNull(received);
        Assert.EndsWith(".png", Assert.Single(received.Images).File);
    }

    [Fact]
    public void BrokenCropFallsBackToWholeImage()
    {
        var board = new Board { Name = "Rognage" };
        board.Images.Add(new BoardImage { File = "a.png", CropLeft = 0.8f, CropRight = 0.2f, CropTop = -3f, CropBottom = 0.5f });
        var path = HandMadeFile(board, ("a.png", File.ReadAllBytes(SourcePng("a.png", 10, 10))));

        var image = Assert.Single(Receiver().Import(path)!.Images);

        Assert.False(image.IsCropped);
    }

    [Fact]
    public void FileThatIsNotABoardIsRefused()
    {
        File.WriteAllText(SharedFile(), "pas un zip");
        var receiver = Receiver();

        Assert.Null(receiver.Import(SharedFile()));
        Assert.Empty(receiver.Boards);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(folder, "receiver")));
        Assert.Contains("not a RefBoardXIV board", Assert.Single(warnings));
    }

    [Fact]
    public void ZipWithoutBoardIsRefused()
    {
        using (var archive = ZipFile.Open(SharedFile(), ZipArchiveMode.Create))
            archive.CreateEntry("photo.png");
        var receiver = Receiver();

        Assert.Null(receiver.Import(SharedFile()));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(folder, "receiver")));
        Assert.Contains("not a RefBoardXIV board", Assert.Single(warnings));
    }

    [Fact]
    public void ExportReplacesAnOlderFileAndLeavesNoTemporaryFile()
    {
        var sender = Sender();
        var board = sender.Create("Tenues");
        File.WriteAllText(SharedFile(), "ancienne version");

        Assert.True(sender.Export(board, SharedFile()));

        Assert.NotNull(Receiver().Import(SharedFile()));
        Assert.False(File.Exists(SharedFile() + ".tmp"));
    }

    [Theory]
    [InlineData("tenues.refboard", true)]
    [InlineData("TENUES.REFBOARD", true)]
    [InlineData("tenues.zip", false)]
    [InlineData("tenues.png", false)]
    public void SharedBoardsAreRecognizedByTheirExtension(string path, bool expected) =>
        Assert.Equal(expected, BoardStore.IsSharedBoard(path));
}
