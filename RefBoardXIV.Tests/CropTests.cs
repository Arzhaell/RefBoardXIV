using System.Numerics;

namespace RefBoardXIV.Tests;

public class CropTests
{
    private const float Tolerance = 0.001f;

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < Tolerance, $"attendu {expected}, obtenu {actual}");

    private static BoardImage Image() => new() { X = 100, Y = 50, Width = 400, Height = 200 };

    [Fact]
    public void CropKeepsImageInPlace()
    {
        var image = Image();
        image.CropTo(new Vector2(200, 100), new Vector2(400, 200));

        // La partie gardée ne bouge pas à l'écran : seule l'image autour disparaît.
        Near(new Vector2(200, 100), image.Min);
        Near(new Vector2(400, 200), image.Max);
        Assert.Equal((0.25f, 0.25f, 0.75f, 0.75f), (image.CropLeft, image.CropTop, image.CropRight, image.CropBottom));
        Assert.True(image.IsCropped);

        // L'image entière, elle, est toujours au même endroit.
        Near(new Vector2(100, 50), image.FullRect.Min);
        Near(new Vector2(500, 250), image.FullRect.Max);
    }

    [Fact]
    public void CropCannotLeaveTheImage()
    {
        var image = Image();
        image.CropTo(new Vector2(0, 0), new Vector2(300, 1000));
        Near(new Vector2(100, 50), image.Min);
        Near(new Vector2(300, 250), image.Max);
        Assert.Equal((0f, 0f), (image.CropLeft, image.CropTop));
        Assert.Equal(1f, image.CropBottom);
    }

    [Fact]
    public void CropSurvivesResizing()
    {
        var image = Image();
        image.CropTo(new Vector2(200, 100), new Vector2(400, 200));
        // Image rognée agrandie deux fois (comme avec ses coins) : l'image entière suit.
        image.Width *= 2;
        image.Height *= 2;
        var (fullMin, fullMax) = image.FullRect;
        Near(new Vector2(800, 400), fullMax - fullMin);
        Near(new Vector2(0, 0), fullMin);
    }

    [Fact]
    public void UncropBringsWholeImageBack()
    {
        var image = Image();
        image.CropTo(new Vector2(150, 60), new Vector2(350, 120));
        image.Uncrop();
        Assert.False(image.IsCropped);
        Near(new Vector2(100, 50), image.Min);
        Near(new Vector2(500, 250), image.Max);
    }

    [Fact]
    public void SnapshotRestoresCancelledCrop()
    {
        var image = Image();
        var backup = image.Snapshot();
        image.CropTo(new Vector2(150, 60), new Vector2(350, 120));
        image.Restore(backup);
        Assert.False(image.IsCropped);
        Assert.Equal((100f, 50f, 400f, 200f), (image.X, image.Y, image.Width, image.Height));
    }

    [Theory]
    [InlineData(1, -50f, 0f, 50f, 0f, 300f, 100f)]
    [InlineData(4 | 8, 30f, 40f, 100f, 0f, 330f, 140f)]
    [InlineData(2, 0f, 500f, 100f, 90f, 300f, 100f)] // ne passe pas sous la taille minimale
    public void DragMovesOnlyChosenEdges(int edges, float dx, float dy, float minX, float minY, float maxX, float maxY)
    {
        var (min, max) = RectEdit.Drag(new Vector2(100, 0), new Vector2(300, 100), (Edges)edges, new Vector2(dx, dy), 10f);
        Near(new Vector2(minX, minY), min);
        Near(new Vector2(maxX, maxY), max);
    }

    [Fact]
    public void DragStaysWithinBounds()
    {
        var bounds = (new Vector2(0, 0), new Vector2(400, 200));
        var (min, max) = RectEdit.Drag(new Vector2(100, 50), new Vector2(300, 150), Edges.Left | Edges.Bottom, new Vector2(-500, 500), 10f, bounds);
        Near(new Vector2(0, 50), min);
        Near(new Vector2(300, 200), max);
    }

    [Fact]
    public void HandlesSitOnCornersAndMiddles()
    {
        var min = new Vector2(0, 0);
        var max = new Vector2(100, 50);
        Near(new Vector2(0, 0), RectEdit.HandlePosition(min, max, Edges.Left | Edges.Top));
        Near(new Vector2(50, 0), RectEdit.HandlePosition(min, max, Edges.Top));
        Near(new Vector2(100, 25), RectEdit.HandlePosition(min, max, Edges.Right));
        Near(new Vector2(100, 50), RectEdit.HandlePosition(min, max, Edges.Right | Edges.Bottom));
        Assert.Equal(8, RectEdit.Handles.Distinct().Count());
    }

    [Fact]
    public void FitShowsWholeRectangle()
    {
        var view = new CanvasView(Vector2.Zero, Vector2.Zero, 1f);
        view.Fit(new Vector2(1000, 1000), new Vector2(1400, 1200), new Vector2(800, 400));
        var min = view.ToScreen(new Vector2(1000, 1000));
        var max = view.ToScreen(new Vector2(1400, 1200));
        Assert.True(min.X >= 0 && min.Y >= 0 && max.X <= 800 && max.Y <= 400);
        Near(new Vector2(400, 200), (min + max) / 2f);
    }
}
