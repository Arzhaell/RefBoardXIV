using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;

namespace RefBoardXIV;

/// <summary>Un tableau de références : des images posées sur un plan infini, et l'endroit où on le regarde.</summary>
public sealed class Board
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Images dans l'ordre d'affichage : la dernière est au-dessus des autres.</summary>
    public List<BoardImage> Images { get; set; } = [];

    // Vue enregistrée : point du tableau affiché dans le coin haut-gauche de la fenêtre, et zoom.
    public float ViewX { get; set; }
    public float ViewY { get; set; }
    public float Zoom { get; set; } = 1f;
}

public sealed class BoardImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nom de la copie de l'image, dans le dossier du tableau.</summary>
    public string File { get; set; } = "";

    // Coin haut-gauche et taille de la partie affichée, en unités du tableau (1 unité = 1 pixel de l'image au zoom 1).
    // Une taille nulle veut dire « pas encore connue » : elle sera prise de l'image au premier affichage.
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    // Rognage : partie de l'image gardée, en fraction de l'image entière (0 à 1 ; 0, 0, 1, 1 = image entière).
    public float CropLeft { get; set; }
    public float CropTop { get; set; }
    public float CropRight { get; set; } = 1f;
    public float CropBottom { get; set; } = 1f;

    [JsonIgnore]
    public Vector2 Min => new(X, Y);

    [JsonIgnore]
    public Vector2 Max => new(X + Width, Y + Height);

    [JsonIgnore]
    public bool HasSize => Width > 0 && Height > 0;

    [JsonIgnore]
    public bool IsCropped => CropLeft > 0 || CropTop > 0 || CropRight < 1 || CropBottom < 1;

    [JsonIgnore]
    public Vector2 UvMin => new(CropLeft, CropTop);

    [JsonIgnore]
    public Vector2 UvMax => new(CropRight, CropBottom);

    /// <summary>Place qu'occuperait l'image entière (sans rognage), en unités du tableau.</summary>
    [JsonIgnore]
    public (Vector2 Min, Vector2 Max) FullRect
    {
        get
        {
            var size = new Vector2(Width / (CropRight - CropLeft), Height / (CropBottom - CropTop));
            var min = new Vector2(X - (CropLeft * size.X), Y - (CropTop * size.Y));
            return (min, min + size);
        }
    }

    public bool Contains(Vector2 point) => HasSize && point.X >= X && point.Y >= Y && point.X <= X + Width && point.Y <= Y + Height;

    /// <summary>Ne garde de l'image que le rectangle donné (en unités du tableau), ramené dans l'image entière.</summary>
    public void CropTo(Vector2 min, Vector2 max)
    {
        var (fullMin, fullMax) = FullRect;
        var size = fullMax - fullMin;
        min = Vector2.Clamp(min, fullMin, fullMax);
        max = Vector2.Clamp(max, fullMin, fullMax);
        CropLeft = (min.X - fullMin.X) / size.X;
        CropTop = (min.Y - fullMin.Y) / size.Y;
        CropRight = (max.X - fullMin.X) / size.X;
        CropBottom = (max.Y - fullMin.Y) / size.Y;
        X = min.X;
        Y = min.Y;
        Width = max.X - min.X;
        Height = max.Y - min.Y;
    }

    /// <summary>Retire le rognage : l'image entière revient, la partie gardée ne bouge pas.</summary>
    public void Uncrop()
    {
        var (fullMin, fullMax) = FullRect;
        CropLeft = CropTop = 0f;
        CropRight = CropBottom = 1f;
        X = fullMin.X;
        Y = fullMin.Y;
        Width = fullMax.X - fullMin.X;
        Height = fullMax.Y - fullMin.Y;
    }

    /// <summary>Copie des valeurs, pour annuler un rognage en cours.</summary>
    public BoardImage Snapshot() => (BoardImage)MemberwiseClone();

    public void Restore(BoardImage snapshot)
    {
        (X, Y, Width, Height) = (snapshot.X, snapshot.Y, snapshot.Width, snapshot.Height);
        (CropLeft, CropTop, CropRight, CropBottom) = (snapshot.CropLeft, snapshot.CropTop, snapshot.CropRight, snapshot.CropBottom);
    }
}
