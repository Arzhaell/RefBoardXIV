using System.Buffers.Binary;
using System.Numerics;

namespace RefBoardXIV.Tests;

/// <summary>Chaque test travaille dans son propre dossier temporaire, effacé à la fin.</summary>
public sealed class BoardStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "RefBoardXIV.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> warnings = [];

    public BoardStoreTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, true);

    private string Root => Path.Combine(folder, "boards");

    private BoardStore NewStore() => new(Root, warnings.Add);

    private string SourcePng(string name, int width, int height)
    {
        var data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void AddedImagesAreCopiedAndPlacedSideBySide()
    {
        var store = NewStore();
        var board = store.Create("Tenues");
        var first = SourcePng("a.png", 100, 50);
        var second = SourcePng("b.PNG", 30, 30);

        var added = store.AddFiles(board, [first, Path.Combine(folder, "notes.txt"), second], new Vector2(10, 20));

        Assert.Equal(2, added.Count);
        Assert.Equal((10f, 20f, 100f, 50f), (added[0].X, added[0].Y, added[0].Width, added[0].Height));
        Assert.Equal(130f, added[1].X); // 10 + 100 + écart de 20
        Assert.EndsWith(".png", added[1].File);
        Assert.All(added, image => Assert.True(File.Exists(store.PathOf(board, image))));

        // Les copies ne dépendent plus des originaux.
        File.Delete(first);
        Assert.True(File.Exists(store.PathOf(board, added[0])));
    }

    [Fact]
    public void BoardsSurviveReloading()
    {
        var store = NewStore();
        var board = store.Create("Logement");
        store.AddFiles(board, [SourcePng("a.png", 64, 32)], Vector2.Zero);
        board.ViewX = 12;
        board.Zoom = 2.5f;
        board.Images[0].X = 77;
        store.MarkChanged(board);
        store.SaveChanged();

        var reloaded = Assert.Single(NewStore().Boards);
        Assert.Equal(board.Id, reloaded.Id);
        Assert.Equal("Logement", reloaded.Name);
        Assert.Equal((12f, 2.5f), (reloaded.ViewX, reloaded.Zoom));
        Assert.Equal((77f, 64f, 32f), (reloaded.Images[0].X, reloaded.Images[0].Width, reloaded.Images[0].Height));
    }

    [Fact]
    public void CropSurvivesReloading()
    {
        var store = NewStore();
        var board = store.Create("Planche");
        var image = store.AddFiles(board, [SourcePng("a.png", 200, 100)], Vector2.Zero)[0];
        image.CropTo(new Vector2(50, 25), new Vector2(150, 75));
        store.Save(board);

        var json = File.ReadAllText(Path.Combine(store.FolderOf(board), BoardStore.BoardFile));
        Assert.DoesNotContain("\"Min\"", json); // valeurs calculées : pas enregistrées
        Assert.DoesNotContain("FullRect", json);

        var reloaded = Assert.Single(NewStore().Boards);
        var reloadedImage = reloaded.Images[0];
        Assert.Equal((0.25f, 0.25f, 0.75f, 0.75f), (reloadedImage.CropLeft, reloadedImage.CropTop, reloadedImage.CropRight, reloadedImage.CropBottom));
        Assert.Equal((50f, 25f, 100f, 50f), (reloadedImage.X, reloadedImage.Y, reloadedImage.Width, reloadedImage.Height));
    }

    [Fact]
    public void OldBoardFilesStillLoad()
    {
        // Tableau d'une version précédente : sans rognage, avec les anciennes valeurs calculées en trop et l'ancien cadre
        // posé sur le tableau (retiré depuis : ignoré).
        var store = NewStore();
        var board = store.Create("Ancien");
        File.WriteAllText(Path.Combine(store.FolderOf(board), BoardStore.BoardFile), $$"""
            { "Id": "{{board.Id}}", "Name": "Ancien", "Images": [ { "Id": "{{Guid.NewGuid()}}", "File": "a.png",
              "X": 1, "Y": 2, "Width": 30, "Height": 40, "Min": {}, "Max": {}, "HasSize": true } ],
              "ViewX": 0, "ViewY": 0, "Zoom": 1, "FrameX": -5, "FrameY": -5, "FrameWidth": 300, "FrameHeight": 200 }
            """);

        var image = Assert.Single(Assert.Single(NewStore().Boards).Images);
        Assert.False(image.IsCropped);
        Assert.Equal((30f, 40f), (image.Width, image.Height));
        Assert.Empty(warnings);
    }

    [Fact]
    public void DetachKeepsCopyUntilDeleted()
    {
        var store = NewStore();
        var board = store.Create("Test");
        var images = store.AddFiles(board, [SourcePng("a.png", 10, 10), SourcePng("b.png", 10, 10)], Vector2.Zero);
        var path = store.PathOf(board, images[0]);

        var index = store.Detach(board, images[0]);
        Assert.Equal(0, index);
        Assert.Single(board.Images);
        Assert.True(File.Exists(path));

        store.Attach(board, images[0], index);
        Assert.Same(images[0], board.Images[0]);

        store.Detach(board, images[0]);
        store.DeleteFile(board, images[0]);
        Assert.False(File.Exists(path));
        Assert.Single(NewStore().Boards[0].Images);
    }

    [Fact]
    public void PastedImageIsSaved()
    {
        var store = NewStore();
        var board = store.Create("Test");
        var data = File.ReadAllBytes(SourcePng("a.png", 40, 20));

        var image = store.AddData(board, data, ".png", new Vector2(5, 5));

        Assert.NotNull(image);
        Assert.Equal(data, File.ReadAllBytes(store.PathOf(board, image)));
        Assert.Equal(40f, image.Width);
    }

    [Fact]
    public void DeletingBoardRemovesOnlyItsFolder()
    {
        var store = NewStore();
        var kept = store.Create("Gardé");
        var deleted = store.Create("Supprimé");
        var original = SourcePng("a.png", 10, 10);
        store.AddFiles(deleted, [original], Vector2.Zero);

        store.Delete(deleted);

        Assert.False(Directory.Exists(store.FolderOf(deleted)));
        Assert.True(Directory.Exists(store.FolderOf(kept)));
        Assert.True(File.Exists(original));
        Assert.Equal("Gardé", Assert.Single(NewStore().Boards).Name);
    }

    [Fact]
    public void DamagedBoardIsSkippedButLeftOnDisk()
    {
        var store = NewStore();
        var board = store.Create("Abîmé");
        var file = Path.Combine(store.FolderOf(board), BoardStore.BoardFile);
        File.WriteAllText(file, "{ pas du json");

        Assert.Empty(NewStore().Boards);
        Assert.Single(warnings);
        Assert.True(File.Exists(file));
    }
}
