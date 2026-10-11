using System.Numerics;

namespace RefBoardXIV.Tests;

public class CanvasViewTests
{
    private const float Tolerance = 0.001f;

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < Tolerance, $"attendu {expected}, obtenu {actual}");

    private static BoardImage Image(float x, float y, float width, float height) => new() { X = x, Y = y, Width = width, Height = height };

    [Fact]
    public void ScreenAndCanvasAreInverse()
    {
        var view = new CanvasView(new Vector2(100, 50), new Vector2(-30, 20), 2.5f);
        var point = new Vector2(12.5f, -7f);
        Near(point, view.ToCanvas(view.ToScreen(point)));
        Near(new Vector2(100, 50), view.ToScreen(new Vector2(-30, 20)));
    }

    [Fact]
    public void ZoomKeepsPointUnderMouse()
    {
        var view = new CanvasView(new Vector2(10, 10), new Vector2(0, 0), 1f);
        var mouse = new Vector2(210, 160);
        var before = view.ToCanvas(mouse);
        view.ZoomAt(mouse, 1.15f);
        view.ZoomAt(mouse, 1.15f);
        Near(before, view.ToCanvas(mouse));
        Assert.Equal(1.3225f, view.Zoom, 4);
    }

    [Fact]
    public void ZoomStaysWithinLimits()
    {
        var view = new CanvasView(Vector2.Zero, Vector2.Zero, 1f);
        view.ZoomAt(Vector2.Zero, 1000f);
        Assert.Equal(CanvasView.MaxZoom, view.Zoom);
        view.ZoomAt(Vector2.Zero, 0.000001f);
        Assert.Equal(CanvasView.MinZoom, view.Zoom);
    }

    [Fact]
    public void PanFollowsMouse()
    {
        var view = new CanvasView(Vector2.Zero, Vector2.Zero, 2f);
        var point = new Vector2(40, 40);
        var screenBefore = view.ToScreen(point);
        view.Pan(new Vector2(30, -10));
        Near(screenBefore + new Vector2(30, -10), view.ToScreen(point));
    }

    [Fact]
    public void FitShowsAllImagesCentered()
    {
        var images = new List<BoardImage> { Image(0, 0, 100, 100), Image(300, 100, 100, 100), new() { X = 5000, Y = 5000 } };
        var view = new CanvasView(Vector2.Zero, Vector2.Zero, 1f);
        var size = new Vector2(800, 400);
        view.Fit(images, size);

        // Les images de taille inconnue (pas encore chargées) ne comptent pas.
        Near(size / 2f, view.ToScreen(new Vector2(200, 100)));
        var min = view.ToScreen(Vector2.Zero);
        var max = view.ToScreen(new Vector2(400, 200));
        Assert.True(min.X >= 0 && min.Y >= 0 && max.X <= size.X && max.Y <= size.Y);
    }

    [Fact]
    public void FitOnEmptyBoardCentersOrigin()
    {
        var view = new CanvasView(Vector2.Zero, new Vector2(500, 500), 3f);
        view.Fit([], new Vector2(800, 400));
        Assert.Equal(1f, view.Zoom);
        Near(new Vector2(400, 200), view.ToScreen(Vector2.Zero));
    }

    [Fact]
    public void HitTestFindsTopImage()
    {
        var bottom = Image(0, 0, 100, 100);
        var top = Image(50, 50, 100, 100);
        List<BoardImage> images = [bottom, top];
        Assert.Same(top, CanvasView.HitTest(images, new Vector2(75, 75)));
        Assert.Same(bottom, CanvasView.HitTest(images, new Vector2(10, 10)));
        Assert.Null(CanvasView.HitTest(images, new Vector2(200, 10)));
    }

    [Fact]
    public void ResizeKeepsOppositeCornerAndProportions()
    {
        var start = new Vector2(100, 100);
        var size = new Vector2(200, 100);

        var (position, resized) = CanvasView.Resize(start, size, Corner.BottomRight, new Vector2(500, 150), 10f);
        Near(start, position);
        Near(new Vector2(400, 200), resized);

        // Coin haut-gauche tiré vers l'extérieur : le coin bas-droit (300, 200) ne bouge pas.
        (position, resized) = CanvasView.Resize(start, size, Corner.TopLeft, new Vector2(-100, 100), 10f);
        Near(new Vector2(300, 200), position + resized);
        Near(new Vector2(400, 200), resized);
    }

    [Fact]
    public void ResizeNeverGoesBelowMinimum()
    {
        var (position, resized) = CanvasView.Resize(new Vector2(0, 0), new Vector2(200, 100), Corner.TopRight, new Vector2(-500, 500), 10f);
        Near(new Vector2(20, 10), resized);
        Near(new Vector2(0, 90), position);
    }
}
