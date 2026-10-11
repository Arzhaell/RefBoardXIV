using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace RefBoardXIV;

/// <summary>
/// Les tableaux sur le disque : un dossier par tableau, avec son fichier <c>board.json</c> et une copie de chaque image
/// (le tableau ne casse pas si tu déplaces ou supprimes les fichiers d'origine).
/// </summary>
internal sealed class BoardStore
{
    public const string BoardFile = "board.json";

    /// <summary>Extensions des images que le jeu sait afficher.</summary>
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    // Écart entre des images ajoutées ensemble, posées côte à côte.
    private const float Gap = 20f;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string root;
    private readonly Action<string> warn;
    private readonly HashSet<Guid> dirty = [];

    public BoardStore(string root, Action<string> warn)
    {
        this.root = root;
        this.warn = warn;
        Directory.CreateDirectory(root);
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (Read(Path.Combine(folder, BoardFile)) is { } board)
                Boards.Add(board);
        }

        Boards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    public List<Board> Boards { get; } = [];

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public string FolderOf(Board board) => Path.Combine(root, board.Id.ToString("N"));

    public string PathOf(Board board, BoardImage image) => Path.Combine(FolderOf(board), image.File);

    public Board Create(string name)
    {
        var board = new Board { Name = name };
        Directory.CreateDirectory(FolderOf(board));
        Boards.Add(board);
        Save(board);
        return board;
    }

    /// <summary>Supprime le tableau et les copies de ses images (les fichiers d'origine ne sont pas touchés).</summary>
    public void Delete(Board board)
    {
        Boards.Remove(board);
        dirty.Remove(board.Id);
        var folder = FolderOf(board);
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    public void Rename(Board board, string name)
    {
        board.Name = name;
        Save(board);
    }

    /// <summary>
    /// Copie des images dans le tableau et les pose côte à côte à partir de <paramref name="position"/> (coin haut-gauche
    /// de la première). Renvoie les images ajoutées ; les fichiers illisibles sont signalés et laissés de côté.
    /// </summary>
    public List<BoardImage> AddFiles(Board board, IEnumerable<string> paths, Vector2 position)
    {
        var added = new List<BoardImage>();
        foreach (var path in paths.Where(IsImage))
        {
            try
            {
                var image = NewImage(Path.GetExtension(path));
                File.Copy(path, PathOf(board, image));
                Place(board, image, ref position);
                added.Add(image);
            }
            catch (Exception e)
            {
                warn($"{Path.GetFileName(path)}: {e.Message}");
            }
        }

        if (added.Count > 0)
            Save(board);
        return added;
    }

    /// <summary>Ajoute une image collée (son contenu, pas un fichier). Null si elle n'a pas pu être enregistrée.</summary>
    public BoardImage? AddData(Board board, byte[] data, string extension, Vector2 position)
    {
        try
        {
            var image = NewImage(extension);
            File.WriteAllBytes(PathOf(board, image), data);
            Place(board, image, ref position);
            Save(board);
            return image;
        }
        catch (Exception e)
        {
            warn(e.Message);
            return null;
        }
    }

    /// <summary>Retire une image du tableau en gardant sa copie (pour pouvoir annuler). Renvoie sa place dans la pile.</summary>
    public int Detach(Board board, BoardImage image)
    {
        var index = board.Images.IndexOf(image);
        board.Images.Remove(image);
        Save(board);
        return index;
    }

    /// <summary>Remet une image retirée à sa place dans la pile.</summary>
    public void Attach(Board board, BoardImage image, int index)
    {
        board.Images.Insert(Math.Clamp(index, 0, board.Images.Count), image);
        Save(board);
    }

    /// <summary>Supprime la copie d'une image retirée du tableau (l'original n'est pas touché).</summary>
    public void DeleteFile(Board board, BoardImage image)
    {
        try
        {
            File.Delete(PathOf(board, image));
        }
        catch (Exception e)
        {
            warn(e.Message);
        }
    }

    /// <summary>Le tableau a changé (vue, image déplacée…) : il sera enregistré au prochain <see cref="SaveChanged"/>.</summary>
    public void MarkChanged(Board board) => dirty.Add(board.Id);

    public void SaveChanged()
    {
        foreach (var board in Boards.Where(b => dirty.Contains(b.Id)))
            Save(board);
        dirty.Clear();
    }

    public void Save(Board board)
    {
        dirty.Remove(board.Id);
        try
        {
            var folder = FolderOf(board);
            Directory.CreateDirectory(folder);
            // Écrit à côté puis remplace : un arrêt brutal du jeu ne laisse jamais un board.json à moitié écrit.
            var path = Path.Combine(folder, BoardFile);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(board, JsonOptions));
            File.Move(temporary, path, true);
        }
        catch (Exception e)
        {
            warn(e.Message);
        }
    }

    private Board? Read(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Board>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            // Fichier abîmé : on le laisse tel quel sur le disque (rien n'est effacé), le tableau n'est juste pas chargé.
            warn($"{path}: {e.Message}");
            return null;
        }
    }

    private static BoardImage NewImage(string extension)
    {
        var image = new BoardImage();
        image.File = image.Id.ToString("N") + extension.ToLowerInvariant();
        return image;
    }

    // Pose l'image à la position donnée (taille lue dans le fichier quand c'est possible) et avance pour la suivante.
    private void Place(Board board, BoardImage image, ref Vector2 position)
    {
        image.X = position.X;
        image.Y = position.Y;
        if (ImageSize.Read(PathOf(board, image)) is var (width, height))
        {
            image.Width = width;
            image.Height = height;
        }

        board.Images.Add(image);
        position.X += (image.HasSize ? image.Width : 0) + Gap;
    }
}
