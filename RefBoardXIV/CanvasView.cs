using System;
using System.Collections.Generic;
using System.Numerics;

namespace RefBoardXIV;

/// <summary>Coins d'une image, pour l'agrandir ou la réduire.</summary>
internal enum Corner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Côtés d'un rectangle qu'une poignée fait bouger (un coin = deux côtés).</summary>
[Flags]
internal enum Edges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>Rectangles qu'on modifie côté par côté : le cadre de la planche, le rognage d'une image.</summary>
internal static class RectEdit
{
    /// <summary>Les huit poignées : quatre coins et le milieu de chaque côté.</summary>
    public static readonly Edges[] Handles =
    [
        Edges.Left | Edges.Top, Edges.Top, Edges.Right | Edges.Top, Edges.Right,
        Edges.Right | Edges.Bottom, Edges.Bottom, Edges.Left | Edges.Bottom, Edges.Left,
    ];

    public static Vector2 HandlePosition(Vector2 min, Vector2 max, Edges handle) => new(
        handle.HasFlag(Edges.Left) ? min.X : handle.HasFlag(Edges.Right) ? max.X : (min.X + max.X) / 2f,
        handle.HasFlag(Edges.Top) ? min.Y : handle.HasFlag(Edges.Bottom) ? max.Y : (min.Y + max.Y) / 2f);

    /// <summary>
    /// Déplace les côtés choisis de <paramref name="delta"/>, sans que le rectangle devienne plus petit que
    /// <paramref name="minSize"/> ni sorte de <paramref name="bounds"/> (s'il y en a).
    /// </summary>
    public static (Vector2 Min, Vector2 Max) Drag(
        Vector2 min, Vector2 max, Edges edges, Vector2 delta, float minSize, (Vector2 Min, Vector2 Max)? bounds = null)
    {
        if (edges.HasFlag(Edges.Left))
            min.X = MathF.Max(MathF.Min(min.X + delta.X, max.X - minSize), bounds?.Min.X ?? float.MinValue);
        if (edges.HasFlag(Edges.Right))
            max.X = MathF.Min(MathF.Max(max.X + delta.X, min.X + minSize), bounds?.Max.X ?? float.MaxValue);
        if (edges.HasFlag(Edges.Top))
            min.Y = MathF.Max(MathF.Min(min.Y + delta.Y, max.Y - minSize), bounds?.Min.Y ?? float.MinValue);
        if (edges.HasFlag(Edges.Bottom))
            max.Y = MathF.Min(MathF.Max(max.Y + delta.Y, min.Y + minSize), bounds?.Max.Y ?? float.MaxValue);
        return (min, max);
    }
}

/// <summary>
/// Passage entre l'écran et le tableau : <see cref="Offset"/> est le point du tableau affiché dans le coin haut-gauche
/// de la zone de dessin (<see cref="Origin"/>, en pixels de l'écran), <see cref="Zoom"/> le nombre de pixels par unité.
/// </summary>
internal struct CanvasView
{
    public const float MinZoom = 0.02f;
    public const float MaxZoom = 50f;

    public Vector2 Origin;
    public Vector2 Offset;
    public float Zoom;

    public CanvasView(Vector2 origin, Vector2 offset, float zoom)
    {
        Origin = origin;
        Offset = offset;
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    public readonly Vector2 ToScreen(Vector2 point) => Origin + ((point - Offset) * Zoom);

    public readonly Vector2 ToCanvas(Vector2 screen) => Offset + ((screen - Origin) / Zoom);

    /// <summary>Zoome en gardant immobile le point du tableau qui est sous <paramref name="screen"/> (la souris).</summary>
    public void ZoomAt(Vector2 screen, float factor)
    {
        var anchor = ToCanvas(screen);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        Offset = anchor - ((screen - Origin) / Zoom);
    }

    /// <summary>Fait glisser le tableau d'autant de pixels que la souris.</summary>
    public void Pan(Vector2 screenDelta) => Offset -= screenDelta / Zoom;

    /// <summary>Cadre toutes les images dans une zone de <paramref name="size"/> pixels, avec une petite marge.</summary>
    public void Fit(IEnumerable<BoardImage> images, Vector2 size)
    {
        if (Bounds(images) is not var (min, max))
        {
            Zoom = 1f;
            Offset = -size / 2f;
            return;
        }

        Fit(min, max, size);
    }

    /// <summary>Cadre un rectangle du tableau dans une zone de <paramref name="size"/> pixels, avec une petite marge.</summary>
    public void Fit(Vector2 min, Vector2 max, Vector2 size)
    {
        var extent = Vector2.Max(max - min, Vector2.One);
        Zoom = Math.Clamp(MathF.Min(size.X / extent.X, size.Y / extent.Y) * 0.92f, MinZoom, MaxZoom);
        Offset = ((min + max) / 2f) - (size / 2f / Zoom);
    }

    /// <summary>Rectangle qui contient toutes les images de taille connue (null s'il n'y en a pas).</summary>
    public static (Vector2 Min, Vector2 Max)? Bounds(IEnumerable<BoardImage> images)
    {
        (Vector2 Min, Vector2 Max)? bounds = null;
        foreach (var image in images)
        {
            if (!image.HasSize)
                continue;
            bounds = bounds is var (min, max) ? (Vector2.Min(min, image.Min), Vector2.Max(max, image.Max)) : (image.Min, image.Max);
        }

        return bounds;
    }

    /// <summary>Image sous un point du tableau : la plus haute (null s'il n'y en a pas).</summary>
    public static BoardImage? HitTest(IReadOnlyList<BoardImage> images, Vector2 point)
    {
        for (var i = images.Count - 1; i >= 0; i--)
        {
            if (images[i].Contains(point))
                return images[i];
        }

        return null;
    }

    public static Vector2 CornerOf(BoardImage image, Corner corner) => corner switch
    {
        Corner.TopLeft => image.Min,
        Corner.TopRight => new Vector2(image.X + image.Width, image.Y),
        Corner.BottomLeft => new Vector2(image.X, image.Y + image.Height),
        _ => image.Max,
    };

    /// <summary>
    /// Nouvelle place d'une image dont on tire un coin jusqu'à <paramref name="point"/> : le coin opposé ne bouge pas et
    /// l'image garde ses proportions. <paramref name="minSize"/> : plus petit côté permis, en unités du tableau.
    /// </summary>
    public static (Vector2 Position, Vector2 Size) Resize(Vector2 start, Vector2 startSize, Corner corner, Vector2 point, float minSize)
    {
        var right = corner is Corner.TopRight or Corner.BottomRight;
        var bottom = corner is Corner.BottomLeft or Corner.BottomRight;
        var anchor = new Vector2(right ? start.X : start.X + startSize.X, bottom ? start.Y : start.Y + startSize.Y);

        // Distance au coin fixe, comptée positivement vers l'extérieur de l'image.
        var reach = new Vector2(right ? point.X - anchor.X : anchor.X - point.X, bottom ? point.Y - anchor.Y : anchor.Y - point.Y);
        var scale = MathF.Max(reach.X / startSize.X, reach.Y / startSize.Y);
        scale = MathF.Max(scale, minSize / MathF.Min(startSize.X, startSize.Y));

        var size = startSize * scale;
        var position = new Vector2(right ? anchor.X : anchor.X - size.X, bottom ? anchor.Y : anchor.Y - size.Y);
        return (position, size);
    }
}
