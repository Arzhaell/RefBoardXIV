using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

    /// <summary>Tableau à partager : un zip avec son <c>board.json</c> et ses images, comme son dossier.</summary>
    public const string ShareExtension = ".refboard";

    /// <summary>Extensions des images que le jeu sait afficher.</summary>
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    // Formats déjà compressés : les recompresser dans le zip ne ferait que prendre du temps.
    private static readonly string[] CompressedExtensions = [".png", ".jpg", ".jpeg", ".jfif", ".gif", ".webp"];

    // Garde-fou à l'import d'un fichier reçu : taille maximale d'une image et du board.json une fois décompressés.
    private const long MaxImageBytes = 512L * 1024 * 1024;
    private const long MaxBoardFileBytes = 16L * 1024 * 1024;

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

    public static bool IsSharedBoard(string path) => Path.GetExtension(path).Equals(ShareExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Le nom donné s'il est libre, sinon « Nom (2) », « Nom (3) »…</summary>
    public string UniqueName(string name)
    {
        var candidate = name;
        for (var number = 2; Boards.Any(b => b.Name == candidate); number++)
            candidate = $"{name} ({number})";
        return candidate;
    }

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

    /// <summary>
    /// Enregistre le tableau dans un seul fichier à partager (<see cref="ShareExtension"/>) : sa disposition, ses
    /// rognages et une copie de ses images. Faux si le fichier n'a pas pu être écrit (l'erreur est signalée).
    /// </summary>
    public bool Export(Board board, string path)
    {
        var temporary = path + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                using (var stream = archive.CreateEntry(BoardFile, CompressionLevel.Optimal).Open())
                    JsonSerializer.Serialize(stream, board, JsonOptions);

                foreach (var file in board.Images.Select(i => i.File).Distinct())
                {
                    var source = Path.Combine(FolderOf(board), file);
                    if (!File.Exists(source))
                        continue;
                    var level = CompressedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())
                        ? CompressionLevel.NoCompression
                        : CompressionLevel.Optimal;
                    archive.CreateEntryFromFile(source, file, level);
                }
            }

            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception e)
        {
            warn($"{Path.GetFileName(path)}: {e.Message}");
            File.Delete(temporary);
            return false;
        }
    }

    /// <summary>
    /// Ajoute un tableau reçu (fichier <see cref="ShareExtension"/>) comme nouveau tableau, avec son nom (rendu unique).
    /// Null si le fichier n'est pas un tableau lisible (l'erreur est signalée, rien n'est gardé).
    /// </summary>
    public Board? Import(string path)
    {
        Board? board = null;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var received = archive.GetEntry(BoardFile) is { } entry && entry.Length <= MaxBoardFileBytes
                ? JsonSerializer.Deserialize<Board>(ReadAll(entry))
                : null;
            if (received == null)
            {
                NotABoard(path);
                return null;
            }

            var name = (received.Name ?? "").Trim();
            if (name.Length == 0)
                name = Path.GetFileNameWithoutExtension(path);
            board = new Board
            {
                Name = UniqueName(name.Length > 100 ? name[..100] : name),
                ViewX = received.ViewX,
                ViewY = received.ViewY,
                Zoom = received.Zoom > 0 ? received.Zoom : 1f,
            };
            Directory.CreateDirectory(FolderOf(board));

            // Chaque image reçoit un nouveau nom de fichier : le nom écrit dans le fichier reçu ne sert qu'à retrouver
            // l'image dans le zip, jamais de chemin sur le disque.
            foreach (var image in received.Images ?? [])
            {
                if (image?.File is not { } file || !IsImage(file) || archive.GetEntry(file) is not { } imageEntry
                    || imageEntry.Length > MaxImageBytes)
                    continue;
                var copy = NewImage(Path.GetExtension(file));
                using (var output = File.Create(PathOf(board, copy)))
                using (var input = imageEntry.Open())
                    CopyAtMost(input, output, imageEntry.Length);

                (copy.X, copy.Y) = (image.X, image.Y);
                (copy.Width, copy.Height) = image.HasSize ? (image.Width, image.Height) : (0f, 0f);
                SetCrop(copy, image);
                board.Images.Add(copy);
            }

            // board.json en dernier : un tableau n'est chargé qu'avec lui, un import interrompu ne laisse rien à moitié.
            Save(board);
            Boards.Add(board);
            return board;
        }
        catch (Exception e)
        {
            // Pas un zip, ou un board.json illisible : ce n'est pas un tableau. Sinon (disque…), l'erreur telle quelle.
            if (e is InvalidDataException or JsonException)
                NotABoard(path);
            else
                warn($"{Path.GetFileName(path)}: {e.Message}");
            if (board != null && Directory.Exists(FolderOf(board)))
                Directory.Delete(FolderOf(board), true);
            return null;
        }
    }

    private void NotABoard(string path) => warn(Loc.T(
        $"{Path.GetFileName(path)} is not a RefBoardXIV board.",
        $"{Path.GetFileName(path)} n'est pas un tableau RefBoardXIV."));

    // Rognage reçu, gardé seulement s'il a un sens (dans l'image, pas vide) ; sinon l'image entière.
    private static void SetCrop(BoardImage copy, BoardImage received)
    {
        var left = Math.Clamp(received.CropLeft, 0f, 1f);
        var top = Math.Clamp(received.CropTop, 0f, 1f);
        var right = Math.Clamp(received.CropRight, 0f, 1f);
        var bottom = Math.Clamp(received.CropBottom, 0f, 1f);
        if (right > left && bottom > top)
            (copy.CropLeft, copy.CropTop, copy.CropRight, copy.CropBottom) = (left, top, right, bottom);
    }

    private static string ReadAll(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var buffer = new MemoryStream();
        CopyAtMost(input, buffer, entry.Length);
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    // Copie en refusant d'aller au-delà de la taille annoncée dans le zip (un fichier trafiqué pourrait mentir).
    private static void CopyAtMost(Stream input, Stream output, long limit)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > limit)
                throw new InvalidDataException("Damaged file: an entry is bigger than announced.");
            output.Write(buffer, 0, read);
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
