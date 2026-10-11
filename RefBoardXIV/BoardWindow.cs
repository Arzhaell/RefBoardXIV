using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.DragDrop;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace RefBoardXIV;

/// <summary>La fenêtre du tableau dans le jeu : les images, le zoom, le déplacement, le rognage et les menus.</summary>
internal sealed class BoardWindow : Window
{
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    // Verrouillé : il ne reste que les images.
    private const ImGuiWindowFlags LockedFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

    private static readonly Vector4 Accent = new(1f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 FrameColor = new(0.45f, 0.8f, 1f, 0.9f);

    // Poignées et plus petit côté d'une image, en pixels de l'écran.
    private const float HandleRadius = 5f;
    private const float MinImageSize = 16f;

    // Au-delà de ce déplacement (pixels), un clic droit est un glissement et n'ouvre pas le menu.
    private const float ClickTolerance = 4f;

    // La vue (zoom, déplacement) est enregistrée un peu après le dernier changement, pas à chaque image.
    private const int SaveDelayMs = 1500;

    private const string DragDropLabel = "RefBoardXIVImages";
    private const string MenuPopup = "##CanvasMenu";
    private const string RenamePopup = "##RenameBoard";
    private const string DeletePopup = "##DeleteBoard";

    private enum DragMode
    {
        None,
        Pan,
        Move,
        Resize,
        Crop,
    }

    private readonly Plugin plugin;

    private Guid? selected;
    private DragMode drag;
    private Corner dragCorner;
    private Edges dragEdges;
    private Vector2 dragStartPoint;
    private Vector2 dragStartPosition;
    private Vector2 dragStartSize;
    private (Vector2 Min, Vector2 Max) dragStartRect;
    private (Vector2 Min, Vector2 Max) dragBounds;
    private float dragDistance;
    private bool rightButton;

    // Image en cours de rognage, et ses valeurs d'avant pour pouvoir annuler.
    private Guid? cropping;
    private BoardImage? cropBackup;
    private readonly HotkeyWatcher enterKey = new();
    private readonly HotkeyWatcher escapeKey = new();

    private Guid? menuImage;
    private Vector2 menuPoint;
    private Vector2 viewCenter;
    private bool fitRequested;
    private long saveAt;
    private bool? appliedLock;

    // Coin de la zone de dessin à l'image précédente, et verrou à ce moment-là.
    private Vector2? lastOrigin;
    private bool lastLocked;

    private string renameInput = "";
    private bool openRename;
    private bool openDelete;

    // Dernière image retirée, gardée sur le disque tant qu'on peut annuler (Ctrl+Z).
    private (Board Board, BoardImage Image, int Index)? removed;

    // Images collées converties en PNG en arrière-plan, ajoutées au tableau à l'image suivante.
    private readonly ConcurrentQueue<PastedImage> converted = new();
    private int converting;

    private sealed record PastedImage(Board Board, byte[] Data, string Extension, Vector2 Point);

    public BoardWindow(Plugin plugin)
        : base("###RefBoardXIVBoard", BaseFlags)
    {
        this.plugin = plugin;
        Size = new Vector2(640, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(240, 160), MaximumSize = new Vector2(float.MaxValue) };
        // Échap sert au jeu (et à annuler un rognage) : il ne ferme pas le tableau.
        RespectCloseHotkey = false;
    }

    private Board Board => plugin.CurrentBoard;

    private BoardStore Store => plugin.Store;

    private float Scale => ImGuiHelpers.GlobalScale;

    public override void PreDraw()
    {
        WindowName = $"{Board.Name}###RefBoardXIVBoard";

        // Le verrou se change aussi dans le menu de Dalamud (barre de titre) : on suit ce choix.
        if (appliedLock != null && IsClickthrough != appliedLock)
            plugin.SetLocked(IsClickthrough);
        var locked = plugin.Configuration.Locked;
        if (appliedLock != locked)
        {
            IsClickthrough = locked;
            IsPinned = locked;
            appliedLock = locked;
        }

        Flags = locked ? BaseFlags | LockedFlags : BaseFlags;
    }

    public override void Draw()
    {
        AddConverted();
        var board = Board;
        var locked = plugin.Configuration.Locked;
        if (locked)
            FinishCrop(board, true);
        else
            DrawToolbar(board);
        DrawCanvas(board, locked);
        DrawPopups(board);

        if (saveAt != 0 && Environment.TickCount64 >= saveAt)
        {
            Store.SaveChanged();
            saveAt = 0;
        }
    }

    public override void OnClose()
    {
        FinishCrop(Board, true);
        Store.SaveChanged();
        // Fermé par sa croix (et pas par le masquage automatique) : c'est ton choix, il est retenu.
        if (!plugin.Visibility.IsAutoHidden)
            plugin.Visibility.SetOpen(false);
    }

    /// <summary>Avant de quitter : supprime pour de bon l'image retirée en dernier, et enregistre ce qui reste.</summary>
    public void Flush()
    {
        // Une image collée en cours de conversion : on attend un peu qu'elle soit prête plutôt que de la perdre.
        SpinWait.SpinUntil(() => Volatile.Read(ref converting) == 0, 5000);
        AddConverted();
        FinishCrop(Board, true);
        FinishRemoval();
        Store.SaveChanged();
    }

    /// <summary>Le tableau affiché va changer : la sélection, le rognage et l'annulation ne concernent que l'ancien.</summary>
    public void OnBoardChanged()
    {
        FinishCrop(Board, true);
        selected = null;
        drag = DragMode.None;
        FinishRemoval();
    }

    private void DrawToolbar(Board board)
    {
        ImGui.SetNextItemWidth(180 * Scale);
        if (ImGui.BeginCombo("##Board", board.Name))
        {
            foreach (var other in Store.Boards)
            {
                ImGui.PushID(other.Id.ToString());
                if (ImGui.Selectable(other.Name, other == board))
                    plugin.SelectBoard(other);
                ImGui.PopID();
            }

            ImGui.Separator();
            if (ImGui.Selectable(Loc.T("Import a board…", "Importer un tableau…")))
                OpenImportDialog();
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Plus, Loc.T("New board", "Nouveau tableau")))
        {
            plugin.SelectBoard(Store.Create(plugin.NewBoardName()));
            StartRename();
        }

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Pen, Loc.T("Rename this board", "Renommer ce tableau")))
            StartRename();

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Trash, Loc.T("Delete this board", "Supprimer ce tableau")))
            openDelete = true;

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.ShareAlt, Loc.T(
                "Share this board: saves it in a single file (images included) to send. The other player imports it from the board list.",
                "Partager ce tableau : l'enregistre dans un seul fichier (images comprises) à envoyer. L'autre joueur l'importe depuis la liste des tableaux.")))
            OpenShareDialog(board);

        ImGui.SameLine(0, 16 * Scale);
        if (IconButton(FontAwesomeIcon.Images, Loc.T("Add images…", "Ajouter des images…")))
            OpenAddDialog(viewCenter);

        ImGui.SameLine();
        if (CroppingImage(board) != null)
        {
            if (IconButton(FontAwesomeIcon.Check, Loc.T("Finish cropping (Enter)", "Valider le rognage (Entrée)")))
                FinishCrop(board, true);
        }
        else
        {
            var image = Selected(board);
            ImGui.BeginDisabled(image is not { HasSize: true });
            if (IconButton(FontAwesomeIcon.Crop, Loc.T(
                    "Crop the selected image (or right click on an image)", "Rogner l'image choisie (ou clic droit sur une image)")) && image != null)
                StartCrop(board, image);
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Expand, Loc.T("Show everything (or double-click the background)", "Tout voir (ou double-clic sur le fond)")))
            fitRequested = true;

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Lock, Loc.T(
                "Lock: only what you see in the window stays (the images, in place), and clicks go to the game. To unlock: /refboard lock.",
                "Verrouiller : seul ce que tu vois dans la fenêtre reste (les images, à leur place), et les clics vont au jeu. Pour déverrouiller : /refboard verrouiller.")))
            plugin.SetLocked(true);
    }

    private static bool IconButton(FontAwesomeIcon icon, string tooltip)
    {
        var clicked = ImGuiComponents.IconButton(icon);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private void DrawCanvas(Board board, bool locked)
    {
        // La fenêtre sert de cadre : verrouillée, la barre de titre et les boutons disparaissent mais la zone des images
        // garde sa place et sa taille. Il reste exactement ce qu'on voyait, rien de ce qui était caché derrière la barre.
        var windowTop = ImGui.GetWindowPos().Y;
        if (locked)
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, windowTop + CanvasTop()));
        else
            RememberCanvasTop(ImGui.GetCursorScreenPos().Y - windowTop);

        var origin = ImGui.GetCursorScreenPos();
        var size = Vector2.Max(ImGui.GetContentRegionAvail(), Vector2.One);
        var view = new CanvasView(origin, new Vector2(board.ViewX, board.ViewY), board.Zoom);
        var viewChanged = false;

        // Si la place de la zone a quand même changé au (dé)verrouillage (mesure pas encore faite), la vue suit le
        // décalage pour que les images ne bougent pas à l'écran.
        if (lastOrigin is { } previous && lastLocked != locked && previous != origin)
        {
            view.Offset += (origin - previous) / view.Zoom;
            viewChanged = true;
        }

        lastOrigin = origin;
        lastLocked = locked;
        if (fitRequested)
        {
            view.Fit(board.Images, size);
            fitRequested = false;
            viewChanged = true;
        }

        // Fichiers glissés depuis l'explorateur de Windows : Dalamud en fait un glisser-déposer d'ImGui (la source),
        // que la zone du tableau accepte (la cible). La source se déclare avant tout élément : son info-bulle en est un.
        if (!locked)
            Plugin.DragDrop.CreateImGuiSource(DragDropLabel, CanDrop, DragTooltip);

        ImGui.InvisibleButton("##Canvas", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        if (!locked)
        {
            viewChanged |= HandleMouse(board, ref view, size);
            HandleKeys(board, view);
            if (Plugin.DragDrop.CreateImGuiTarget(DragDropLabel, out var files, out _))
                AddFiles(board, files, view.ToCanvas(ImGui.GetMousePos()));
        }

        viewCenter = view.ToCanvas(origin + (size / 2f));
        DrawBoard(board, view, origin, size, locked);

        if (viewChanged)
        {
            board.ViewX = view.Offset.X;
            board.ViewY = view.Offset.Y;
            board.Zoom = view.Zoom;
            MarkChanged(board);
        }
    }

    // Distance entre le haut de la fenêtre et la zone des images, tableau déverrouillé : mesurée, sinon estimée
    // (barre de titre, marge, ligne de boutons).
    private float CanvasTop()
    {
        if (plugin.Configuration.CanvasTop > 0)
            return plugin.Configuration.CanvasTop;
        var style = ImGui.GetStyle();
        return (ImGui.GetFrameHeight() * 2f) + style.WindowPadding.Y + style.ItemSpacing.Y;
    }

    private void RememberCanvasTop(float top)
    {
        if (MathF.Abs(top - plugin.Configuration.CanvasTop) < 0.5f)
            return;
        plugin.Configuration.CanvasTop = top;
        plugin.Configuration.Save();
    }

    private static bool CanDrop(IDragDropManager drag) => drag.Files.Any(f => BoardStore.IsImage(f) || BoardStore.IsSharedBoard(f));

    private static bool DragTooltip(IDragDropManager drag)
    {
        var images = drag.Files.Count(BoardStore.IsImage);
        var boards = drag.Files.Count(BoardStore.IsSharedBoard);
        if (images > 0)
            ImGui.TextUnformatted(Loc.T($"Add {images} image(s) to the board", $"Ajouter {images} image(s) au tableau"));
        if (boards > 0)
            ImGui.TextUnformatted(Loc.T($"Import {boards} shared board(s)", $"Importer {boards} tableau(x) partagé(s)"));
        return true;
    }

    private bool HandleMouse(Board board, ref CanvasView view, Vector2 size)
    {
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var point = view.ToCanvas(mouse);
        var hovered = ImGui.IsItemHovered();
        var changed = false;

        if (hovered && io.MouseWheel != 0)
        {
            view.ZoomAt(mouse, MathF.Pow(1.15f, io.MouseWheel));
            changed = true;
        }

        if (ImGui.IsItemActivated())
        {
            dragDistance = 0;
            rightButton = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
            drag = ImGui.IsMouseClicked(ImGuiMouseButton.Left) ? StartLeftDrag(board, view, mouse, point) : DragMode.Pan;
        }

        if (ImGui.IsItemActive())
        {
            dragDistance += io.MouseDelta.Length();
            changed |= ContinueDrag(board, ref view, point);
        }

        if (ImGui.IsItemDeactivated())
        {
            if (drag is not (DragMode.None or DragMode.Pan) && dragDistance > 0)
                Store.Save(board);
            if (rightButton && dragDistance < ClickTolerance)
                OpenMenu(board, point);
            drag = DragMode.None;
        }

        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && CanvasView.HitTest(board.Images, point) == null)
        {
            view.Fit(board.Images, size);
            changed = true;
        }

        UpdateCursor(board, view, mouse, hovered);
        return changed;
    }

    // Ce que le clic gauche attrape, du plus précis au plus large : poignées du rognage, de l'image choisie, puis une
    // image, et sinon le fond (pour se déplacer).
    private DragMode StartLeftDrag(Board board, CanvasView view, Vector2 mouse, Vector2 point)
    {
        dragStartPoint = point;

        if (CroppingImage(board) is { } crop)
        {
            if (EdgeHandleUnder(crop.Min, crop.Max, view, mouse) is { } cropEdges)
            {
                dragEdges = cropEdges;
                dragStartRect = (crop.Min, crop.Max);
                dragBounds = crop.FullRect;
                return DragMode.Crop;
            }

            if (crop.Contains(point))
            {
                RememberImage(crop);
                return DragMode.Move;
            }

            // Un clic à côté valide le rognage.
            FinishCrop(board, true);
        }

        if (Selected(board) is { } current && CornerUnder(current, view, mouse) is { } corner)
        {
            dragCorner = corner;
            RememberImage(current);
            return DragMode.Resize;
        }

        var image = CanvasView.HitTest(board.Images, point);
        if (image == null)
        {
            selected = null;
            return DragMode.Pan;
        }

        selected = image.Id;
        MoveInStack(board, image, true);
        RememberImage(image);
        return DragMode.Move;
    }

    private bool ContinueDrag(Board board, ref CanvasView view, Vector2 point)
    {
        var delta = point - dragStartPoint;
        var image = Selected(board);
        switch (drag)
        {
            case DragMode.Pan when ImGui.GetIO().MouseDelta != Vector2.Zero:
                view.Pan(ImGui.GetIO().MouseDelta);
                return true;
            case DragMode.Move when image != null:
                image.X = dragStartPosition.X + delta.X;
                image.Y = dragStartPosition.Y + delta.Y;
                break;
            case DragMode.Resize when image != null:
                var (position, size) = CanvasView.Resize(dragStartPosition, dragStartSize, dragCorner, point, MinImageSize * Scale / view.Zoom);
                (image.X, image.Y, image.Width, image.Height) = (position.X, position.Y, size.X, size.Y);
                break;
            case DragMode.Crop when CroppingImage(board) is { } crop:
                var (cropMin, cropMax) = RectEdit.Drag(dragStartRect.Min, dragStartRect.Max, dragEdges, delta, MinImageSize * Scale / view.Zoom, dragBounds);
                crop.CropTo(cropMin, cropMax);
                break;
        }

        return false;
    }

    private void RememberImage(BoardImage image)
    {
        dragStartPosition = image.Min;
        dragStartSize = new Vector2(image.Width, image.Height);
    }

    private float HandleReach => (HandleRadius + 3f) * Scale;

    // Coin de l'image sous la souris (pour la redimensionner).
    private Corner? CornerUnder(BoardImage image, CanvasView view, Vector2 mouse)
    {
        foreach (var corner in Enum.GetValues<Corner>())
        {
            if (Vector2.Distance(view.ToScreen(CanvasView.CornerOf(image, corner)), mouse) <= HandleReach)
                return corner;
        }

        return null;
    }

    // Poignée d'un rectangle du tableau (coin ou milieu d'un côté) sous la souris.
    private Edges? EdgeHandleUnder(Vector2 min, Vector2 max, CanvasView view, Vector2 mouse)
    {
        foreach (var handle in RectEdit.Handles)
        {
            if (Vector2.Distance(view.ToScreen(RectEdit.HandlePosition(min, max, handle)), mouse) <= HandleReach)
                return handle;
        }

        return null;
    }

    private void UpdateCursor(Board board, CanvasView view, Vector2 mouse, bool hovered)
    {
        ImGuiMouseCursor? cursor = drag switch
        {
            DragMode.Resize => CornerCursor(dragCorner),
            DragMode.Crop => EdgesCursor(dragEdges),
            DragMode.Pan or DragMode.Move when ImGui.IsItemActive() => ImGuiMouseCursor.ResizeAll,
            _ => null,
        };

        if (cursor == null && hovered && drag == DragMode.None)
        {
            if (CroppingImage(board) is { } crop && EdgeHandleUnder(crop.Min, crop.Max, view, mouse) is { } cropEdges)
                cursor = EdgesCursor(cropEdges);
            else if (Selected(board) is { } image && CroppingImage(board) == null && CornerUnder(image, view, mouse) is { } corner)
                cursor = CornerCursor(corner);
        }

        if (cursor is { } value)
            ImGui.SetMouseCursor(value);
    }

    private static ImGuiMouseCursor CornerCursor(Corner corner) =>
        corner is Corner.TopLeft or Corner.BottomRight ? ImGuiMouseCursor.ResizeNwse : ImGuiMouseCursor.ResizeNesw;

    private static ImGuiMouseCursor EdgesCursor(Edges edges) => edges switch
    {
        Edges.Left | Edges.Top or Edges.Right | Edges.Bottom => ImGuiMouseCursor.ResizeNwse,
        Edges.Right | Edges.Top or Edges.Left | Edges.Bottom => ImGuiMouseCursor.ResizeNesw,
        Edges.Left or Edges.Right => ImGuiMouseCursor.ResizeEw,
        _ => ImGuiMouseCursor.ResizeNs,
    };

    // Raccourcis du tableau : actifs quand il a été cliqué en dernier ou que la souris est dessus (après un clic dans le
    // jeu, c'est le jeu qui a le clavier).
    private void HandleKeys(Board board, CanvasView view)
    {
        var focused = ImGui.IsWindowFocused();
        var hovered = ImGui.IsWindowHovered();
        var active = (focused || hovered) && plugin.Shortcuts.Listening;

        // Entrée valide le rognage, Échap l'annule.
        var enter = enterKey.Update(active && Shortcuts.IsKeyDown(VirtualKey.RETURN));
        var escape = escapeKey.Update(active && Shortcuts.IsKeyDown(VirtualKey.ESCAPE));
        if (!focused && !hovered)
            return;
        if (CroppingImage(board) != null && (enter || escape))
            FinishCrop(board, enter);

        var shortcuts = plugin.Shortcuts;
        if (shortcuts.WasPressed(ShortcutAction.Delete) && Selected(board) is { } image)
            Remove(board, image);
        if (shortcuts.WasPressed(ShortcutAction.Undo))
            Undo();
        if (shortcuts.WasPressed(ShortcutAction.Paste))
            Paste(board, hovered ? view.ToCanvas(ImGui.GetIO().MousePos) : viewCenter, focused);
        if (shortcuts.WasPressed(ShortcutAction.Fit))
            fitRequested = true;
        if (shortcuts.WasPressed(ShortcutAction.Crop))
        {
            if (CroppingImage(board) != null)
                FinishCrop(board, true);
            else if (Selected(board) is { HasSize: true } toCrop)
                StartCrop(board, toCrop);
        }
    }

    private void DrawBoard(Board board, CanvasView view, Vector2 origin, Vector2 size, bool locked)
    {
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + size;
        drawList.PushClipRect(origin, end, true);
        DrawImages(drawList, board, view, origin, end);

        if (!locked)
        {
            if (CroppingImage(board) is { } crop)
                DrawCropHandles(drawList, view, crop);
            else if (Selected(board) is { HasSize: true } chosen)
                DrawSelection(drawList, view, chosen);
            DrawHints(drawList, board, origin, size, end);
        }

        if (!locked || plugin.Configuration.FrameWhenLocked)
            DrawFrame(drawList, origin, end, !locked);

        drawList.PopClipRect();
    }

    // Cadre fixe autour de la zone des images : exactement ce qui reste une fois verrouillé. Déverrouillé, sa taille à
    // l'écran s'affiche dans le coin bas-droit.
    private void DrawFrame(ImDrawListPtr drawList, Vector2 origin, Vector2 end, bool showSize)
    {
        var thickness = 1.5f * Scale;
        var inset = new Vector2(thickness / 2f);
        drawList.AddRect(origin + inset, end - inset, ImGui.GetColorU32(FrameColor), 0f, ImDrawFlags.None, thickness);
        if (!showSize)
            return;

        var size = end - origin;
        var label = $"{size.X:0} × {size.Y:0} px";
        var padding = new Vector2(5f, 2f) * Scale;
        var labelMax = end - new Vector2(thickness);
        var labelMin = labelMax - ImGui.CalcTextSize(label) - (padding * 2f);
        drawList.AddRectFilled(labelMin, labelMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), 3f * Scale);
        drawList.AddText(labelMin + padding, ImGui.GetColorU32(FrameColor), label);
    }

    private void DrawImages(ImDrawListPtr drawList, Board board, CanvasView view, Vector2 clipMin, Vector2 clipMax)
    {
        var opacity = Math.Clamp(plugin.Configuration.ImageOpacity, 0.1f, 1f);
        var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, opacity));
        var cropping = CroppingImage(board);
        foreach (var image in board.Images)
        {
            var path = Store.PathOf(board, image);
            if (Plugin.TextureProvider.GetFromFile(path).TryGetWrap(out var texture, out _))
            {
                // Taille pas encore connue (format dont l'en-tête n'est pas lu) : celle de l'image.
                if (!image.HasSize)
                {
                    image.Width = texture.Width * (image.CropRight - image.CropLeft);
                    image.Height = texture.Height * (image.CropBottom - image.CropTop);
                    MarkChanged(board);
                }

                // En cours de rognage : l'image entière en grisé, sous la partie gardée.
                if (image == cropping)
                {
                    var (fullMin, fullMax) = image.FullRect;
                    drawList.AddImage(texture.Handle, view.ToScreen(fullMin), view.ToScreen(fullMax), Vector2.Zero, Vector2.One,
                        ImGui.GetColorU32(new Vector4(0.45f, 0.45f, 0.45f, opacity * 0.6f)));
                }

                var min = view.ToScreen(image.Min);
                var max = view.ToScreen(image.Max);
                if (max.X >= clipMin.X && max.Y >= clipMin.Y && min.X <= clipMax.X && min.Y <= clipMax.Y)
                    drawList.AddImage(texture.Handle, min, max, image.UvMin, image.UvMax, tint);
            }
            else if (image.HasSize)
            {
                // En cours de chargement, ou fichier disparu (« ? »).
                var min = view.ToScreen(image.Min);
                var max = view.ToScreen(image.Max);
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f)));
                if (!File.Exists(path))
                    drawList.AddText((min + max) / 2f, ImGui.GetColorU32(ImGuiColors.DalamudRed), "?");
            }
        }
    }

    private void DrawHints(ImDrawListPtr drawList, Board board, Vector2 origin, Vector2 size, Vector2 end)
    {
        var grey = ImGui.GetColorU32(ImGuiColors.DalamudGrey);
        if (board.Images.Count == 0)
        {
            var hint = Loc.T(
                "Drag images here from the Windows explorer, or paste them.",
                "Glisse des images ici depuis l'explorateur de Windows, ou colle-les.");
            drawList.AddText(origin + ((size - ImGui.CalcTextSize(hint)) / 2f), grey, hint);
        }

        var line = origin + new Vector2(8f * Scale);
        if (CroppingImage(board) != null)
        {
            drawList.AddText(line, ImGui.GetColorU32(Accent), Loc.T(
                "Cropping: drag the edges. Enter or a click elsewhere: done. Esc: cancel.",
                "Rognage : tire les bords. Entrée ou un clic à côté : valider. Échap : annuler."));
            line.Y += ImGui.GetTextLineHeightWithSpacing();
        }

        if (Volatile.Read(ref converting) > 0)
            drawList.AddText(line, grey, Loc.T("Converting the pasted image…", "Conversion de l'image collée…"));

        if (Plugin.DragDrop.IsDragging)
            drawList.AddRect(origin, end, ImGui.GetColorU32(Accent), 0f, ImDrawFlags.None, 3f * Scale);
    }

    private void DrawSelection(ImDrawListPtr drawList, CanvasView view, BoardImage image)
    {
        var color = ImGui.GetColorU32(Accent);
        drawList.AddRect(view.ToScreen(image.Min), view.ToScreen(image.Max), color, 0f, ImDrawFlags.None, 2f * Scale);
        foreach (var corner in Enum.GetValues<Corner>())
            DrawHandle(drawList, view.ToScreen(CanvasView.CornerOf(image, corner)), color);
    }

    private void DrawCropHandles(ImDrawListPtr drawList, CanvasView view, BoardImage image)
    {
        var color = ImGui.GetColorU32(Accent);
        var (fullMin, fullMax) = image.FullRect;
        drawList.AddRect(view.ToScreen(fullMin), view.ToScreen(fullMax), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)));
        drawList.AddRect(view.ToScreen(image.Min), view.ToScreen(image.Max), color, 0f, ImDrawFlags.None, 2f * Scale);
        foreach (var handle in RectEdit.Handles)
            DrawHandle(drawList, view.ToScreen(RectEdit.HandlePosition(image.Min, image.Max, handle)), color);
    }

    private void DrawHandle(ImDrawListPtr drawList, Vector2 center, uint color)
    {
        var half = new Vector2(HandleRadius * Scale);
        drawList.AddRectFilled(center - half, center + half, color);
    }

    private void OpenMenu(Board board, Vector2 point)
    {
        menuImage = CanvasView.HitTest(board.Images, point)?.Id;
        if (menuImage != null)
            selected = menuImage;
        menuPoint = point;
        ImGui.OpenPopup(MenuPopup);
    }

    private void DrawPopups(Board board)
    {
        if (ImGui.BeginPopup(MenuPopup))
        {
            DrawMenu(board);
            ImGui.EndPopup();
        }

        if (openRename)
        {
            ImGui.OpenPopup(RenamePopup);
            openRename = false;
        }

        if (ImGui.BeginPopup(RenamePopup))
        {
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(220 * Scale);
            var entered = ImGui.InputText("##Name", ref renameInput, 100, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if (ImGui.Button("OK") || entered)
            {
                if (renameInput.Trim().Length > 0)
                    Store.Rename(board, renameInput.Trim());
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (openDelete)
        {
            ImGui.OpenPopup(DeletePopup);
            openDelete = false;
        }

        if (ImGui.BeginPopup(DeletePopup))
        {
            ImGui.TextUnformatted(Loc.T(
                $"Delete the board \"{board.Name}\" and its {board.Images.Count} image(s)?",
                $"Supprimer le tableau « {board.Name} » et ses {board.Images.Count} image(s) ?"));
            ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T(
                "Your original files are not touched.", "Tes fichiers d'origine ne sont pas touchés."));
            if (ImGui.Button(Loc.T("Delete", "Supprimer")))
            {
                OnBoardChanged();
                plugin.DeleteBoard(board);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Cancel", "Annuler")))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawMenu(Board board)
    {
        var shortcuts = plugin.Shortcuts;
        if (board.Images.FirstOrDefault(i => i.Id == menuImage) is { } image)
        {
            if (cropping == image.Id)
            {
                if (ImGui.MenuItem(Loc.T("Finish cropping", "Valider le rognage"), Loc.T("Enter", "Entrée")))
                    FinishCrop(board, true);
                if (ImGui.MenuItem(Loc.T("Cancel cropping", "Annuler le rognage"), Loc.T("Esc", "Échap")))
                    FinishCrop(board, false);
            }
            else
            {
                if (ImGui.MenuItem(Loc.T("Crop", "Rogner"), shortcuts.MenuLabel(ShortcutAction.Crop), false, image.HasSize))
                    StartCrop(board, image);
                if (image.IsCropped && ImGui.MenuItem(Loc.T("Whole image (undo the crop)", "Image entière (défaire le rognage)")))
                {
                    image.Uncrop();
                    Store.Save(board);
                }
            }

            if (ImGui.MenuItem(Loc.T("Original size", "Taille d'origine")))
                RestoreSize(board, image);
            if (ImGui.MenuItem(Loc.T("Bring to front", "Mettre devant")))
                MoveInStack(board, image, true);
            if (ImGui.MenuItem(Loc.T("Send to back", "Mettre derrière")))
                MoveInStack(board, image, false);
            if (ImGui.MenuItem(Loc.T("Delete", "Supprimer"), shortcuts.MenuLabel(ShortcutAction.Delete)))
                Remove(board, image);
            ImGui.Separator();
        }

        if (ImGui.MenuItem(Loc.T("Paste", "Coller"), shortcuts.MenuLabel(ShortcutAction.Paste)))
            Paste(board, menuPoint);
        if (ImGui.MenuItem(Loc.T("Add images…", "Ajouter des images…")))
            OpenAddDialog(menuPoint);
        if (ImGui.MenuItem(Loc.T("Undo delete", "Annuler la suppression"), shortcuts.MenuLabel(ShortcutAction.Undo), false, removed?.Board == board))
            Undo();
        if (ImGui.MenuItem(Loc.T("Show everything", "Tout voir"), shortcuts.MenuLabel(ShortcutAction.Fit)))
            fitRequested = true;
    }

    private void StartRename()
    {
        renameInput = Board.Name;
        openRename = true;
    }

    private BoardImage? Selected(Board board) => selected is { } id ? board.Images.FirstOrDefault(i => i.Id == id) : null;

    private BoardImage? CroppingImage(Board board) => cropping is { } id ? board.Images.FirstOrDefault(i => i.Id == id) : null;

    private void StartCrop(Board board, BoardImage image)
    {
        FinishCrop(board, true);
        cropping = image.Id;
        cropBackup = image.Snapshot();
        selected = image.Id;
    }

    // Termine le rognage en cours : gardé (Entrée, clic à côté…) ou annulé (Échap).
    private void FinishCrop(Board board, bool keep)
    {
        if (cropping == null)
            return;
        if (!keep && CroppingImage(board) is { } image && cropBackup != null)
            image.Restore(cropBackup);
        cropping = null;
        cropBackup = null;
        Store.Save(board);
    }

    private void MarkChanged(Board board)
    {
        Store.MarkChanged(board);
        saveAt = Environment.TickCount64 + SaveDelayMs;
    }

    // Met l'image au-dessus (ou en dessous) de toutes les autres.
    private void MoveInStack(Board board, BoardImage image, bool front)
    {
        var index = board.Images.IndexOf(image);
        if (index < 0 || index == (front ? board.Images.Count - 1 : 0))
            return;
        board.Images.RemoveAt(index);
        if (front)
            board.Images.Add(image);
        else
            board.Images.Insert(0, image);
        MarkChanged(board);
    }

    // Taille d'origine de la partie gardée (toute l'image si elle n'est pas rognée).
    private void RestoreSize(Board board, BoardImage image)
    {
        var path = Store.PathOf(board, image);
        Vector2? natural = ImageSize.Read(path) is var (width, height) ? new Vector2(width, height)
            : Plugin.TextureProvider.GetFromFile(path).TryGetWrap(out var texture, out _) ? new Vector2(texture.Width, texture.Height)
            : null;
        if (natural is { } full)
        {
            image.Width = full.X * (image.CropRight - image.CropLeft);
            image.Height = full.Y * (image.CropBottom - image.CropTop);
        }

        Store.Save(board);
    }

    private void Remove(Board board, BoardImage image)
    {
        if (cropping == image.Id)
            FinishCrop(board, true);
        FinishRemoval();
        removed = (board, image, Store.Detach(board, image));
        selected = null;
    }

    private void Undo()
    {
        if (removed is not var (board, image, index) || board != Board)
            return;
        Store.Attach(board, image, index);
        selected = image.Id;
        removed = null;
    }

    private void FinishRemoval()
    {
        if (removed is var (board, image, _) && Store.Boards.Contains(board))
            Store.DeleteFile(board, image);
        removed = null;
    }

    // <paramref name="complain"/> : prévenir s'il n'y a rien à coller. Pas quand la souris ne fait que passer sur le
    // tableau : le Ctrl+V était peut-être pour la discussion du jeu.
    private void Paste(Board board, Vector2 point, bool complain = true)
    {
        var content = Clipboard.Read();
        if (content?.Png is { } png)
        {
            AddPasted(new PastedImage(board, png, ".png", point));
        }
        else if (content?.Dib is { } dib)
        {
            // Conversion en PNG en arrière-plan (un moment sur une grande image) ; l'image arrive dans le tableau ensuite.
            Interlocked.Increment(ref converting);
            Task.Run(() =>
            {
                try
                {
                    var (data, extension) = Clipboard.ImageFromDib(dib);
                    converted.Enqueue(new PastedImage(board, data, extension, point));
                }
                catch (Exception e)
                {
                    Plugin.Log.Error(e, "Could not convert the pasted image.");
                }
                finally
                {
                    Interlocked.Decrement(ref converting);
                }
            });
        }
        else if (content is { Files.Count: > 0 })
        {
            AddFiles(board, content.Files, point);
        }
        else if (complain)
        {
            Plugin.ChatGui.PrintError(Loc.T(
                "Nothing to paste: copy an image, or image files in the Windows explorer.",
                "Rien à coller : copie une image, ou des fichiers d'images dans l'explorateur de Windows."));
        }
    }

    // Ajoute les images collées dont la conversion est finie (le tableau a pu être supprimé entre-temps).
    private void AddConverted()
    {
        while (converted.TryDequeue(out var pasted))
            AddPasted(pasted);
    }

    private void AddPasted(PastedImage pasted)
    {
        if (!Store.Boards.Contains(pasted.Board))
            return;
        if (Store.AddData(pasted.Board, pasted.Data, pasted.Extension, pasted.Point) is { } image && pasted.Board == Board)
            selected = image.Id;
    }

    // Images glissées, collées ou choisies ; un tableau partagé (.refboard) parmi elles devient un nouveau tableau.
    private void AddFiles(Board board, IReadOnlyList<string> files, Vector2 point)
    {
        var shared = files.Where(BoardStore.IsSharedBoard).ToList();
        var images = files.Where(f => !BoardStore.IsSharedBoard(f)).ToList();
        if (shared.Count > 0 && !images.Any(BoardStore.IsImage))
        {
            shared.ForEach(ImportBoard);
            return;
        }

        var added = Store.AddFiles(board, images, point);
        if (added.Count > 0)
            selected = added[^1].Id;
        else
            Plugin.ChatGui.PrintError(Loc.T(
                $"No image to add (accepted: {string.Join(", ", BoardStore.ImageExtensions)}).",
                $"Aucune image à ajouter (formats acceptés : {string.Join(", ", BoardStore.ImageExtensions)})."));
        shared.ForEach(ImportBoard);
    }

    private void OpenAddDialog(Vector2 point)
    {
        var board = Board;
        var filter = $"Images{{{string.Join(",", BoardStore.ImageExtensions)}}}";
        plugin.FileDialog.OpenFileDialog(Loc.T("Add images", "Ajouter des images"), filter, (ok, paths) =>
        {
            if (ok && paths.Count > 0 && Store.Boards.Contains(board))
                AddFiles(board, paths, point);
        }, 50, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
    }

    private void OpenShareDialog(Board board)
    {
        FinishCrop(board, true);
        var invalid = Path.GetInvalidFileNameChars();
        var fileName = new string(board.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        plugin.FileDialog.SaveFileDialog(Loc.T("Share this board", "Partager ce tableau"), SharedFilter, fileName,
            BoardStore.ShareExtension, (ok, path) =>
            {
                if (!ok || !Store.Boards.Contains(board) || !Store.Export(board, path))
                    return;
                var megabytes = new FileInfo(path).Length / (1024d * 1024d);
                Plugin.ChatGui.Print(Loc.T(
                    $"Board \"{board.Name}\" saved to share ({megabytes:0.0} MB): {path}",
                    $"Tableau « {board.Name} » enregistré pour le partage ({megabytes:0.0} Mo) : {path}"));
                ShowInExplorer(path);
            }, DownloadsFolder());
    }

    private void OpenImportDialog()
    {
        plugin.FileDialog.OpenFileDialog(Loc.T("Import a board", "Importer un tableau"), SharedFilter, (ok, paths) =>
        {
            if (ok)
                paths.ForEach(ImportBoard);
        }, 10, DownloadsFolder());
    }

    private static string SharedFilter => $"RefBoardXIV{{{BoardStore.ShareExtension}}}";

    private void ImportBoard(string path)
    {
        if (Store.Import(path) is not { } imported)
            return;
        plugin.SelectBoard(imported);
        fitRequested = true;
        Plugin.ChatGui.Print(Loc.T(
            $"Board \"{imported.Name}\" imported ({imported.Images.Count} image(s)).",
            $"Tableau « {imported.Name} » importé ({imported.Images.Count} image(s))."));
    }

    // Ouvre l'explorateur sur le fichier partagé, prêt à être glissé dans une discussion (Discord…).
    private static void ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Could not open the explorer.");
        }
    }

    // Les fichiers reçus (Discord, navigateur) arrivent en général dans Téléchargements.
    private static string DownloadsFolder()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
